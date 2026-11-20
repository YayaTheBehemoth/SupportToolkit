using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Transport.Dtos;

namespace SupportToolkit.Providers.Dolibarr.Transport;

/// <summary>
/// Dolibarr could not be read, or returned something unusable. Messages
/// are operator-safe: no response body, URL, key, customer or contract
/// identifier.
/// </summary>
public sealed class DolibarrApiException(string message, Exception? inner = null)
    : InvalidOperationException(message, inner);

/// <summary>
/// Reads one Dolibarr: every contract with its lines, and every customer.
/// Reads are paged to the end; a list is complete or the read fails.
/// </summary>
public sealed class DolibarrApiClient
{
    public const int PageSize = 100;

    /// <summary>A guard against a server that ignores the page number.</summary>
    private const int MaximumPages = 10_000;

    private readonly HttpClient _httpClient;
    private readonly ResolvedDolibarrConnection _connection;
    private readonly OperationalLogger? _logger;

    public DolibarrApiClient(
        HttpClient httpClient,
        ResolvedDolibarrConnection connection,
        OperationalLogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger;
    }

    internal Task<IReadOnlyList<DolibarrContractDto>> GetContractsAsync(
        CancellationToken cancellationToken) =>
        GetAllAsync<DolibarrContractDto>("contracts", "", contract => contract.Id, cancellationToken);

    /// <summary>Third parties that are customers (mode=1).</summary>
    internal Task<IReadOnlyList<DolibarrCustomerDto>> GetCustomersAsync(
        CancellationToken cancellationToken) =>
        GetAllAsync<DolibarrCustomerDto>("thirdparties", "&mode=1", customer => customer.Id, cancellationToken);

    private async Task<IReadOnlyList<T>> GetAllAsync<T>(
        string resource,
        string filter,
        Func<T, string?> idOf,
        CancellationToken cancellationToken)
    {
        var all = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; page < MaximumPages; page++)
        {
            var path =
                $"{resource}?limit={PageSize}&page={page}&sortfield=t.rowid&sortorder=ASC{filter}";

            var items = await GetPageAsync<T>(path, cancellationToken);

            foreach (var item in items)
            {
                var id = idOf(item);

                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                {
                    throw new DolibarrApiException(
                        $"Dolibarr returned {resource} without an ID, or the same " +
                        "one twice; the list cannot be trusted to be complete.");
                }

                all.Add(item);
            }

            if (items.Count < PageSize)
            {
                return all;
            }
        }

        throw new DolibarrApiException(
            $"Dolibarr kept returning {resource} past {MaximumPages} pages.");
    }

    private static async Task<bool> IsEmptyResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document =
                JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            return document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.Number
                && code.GetInt32() == 404
                && error.TryGetProperty("message", out var message)
                && message.GetString() is { } text
                && text.StartsWith("Not Found: No ", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<T>> GetPageAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(HttpMethod.Get, new Uri(_connection.ApiRoot, path));

        request.Headers.Add("DOLAPIKEY", _connection.ApiKey);
        request.Headers.Accept.ParseAdd("application/json");

        // The resource only: no host, no key.
        var resource = path[..path.IndexOf('?')];

        _logger?.Debug($"GET {resource}");

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new DolibarrApiException("Dolibarr could not be reached.", exception);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new DolibarrApiException("A Dolibarr request timed out.", exception);
        }

        using (response)
        {
            _logger?.Debug($"GET {resource} -> {(int)response.StatusCode}");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new DolibarrApiException(
                    $"Dolibarr refused the API key (HTTP {(int)response.StatusCode}). " +
                    "The key is wrong, or its user is disabled or lacks read " +
                    "permission on contracts and customers.");
            }

            // Some resources (third parties) answer an empty result with
            // 404 "Not Found: No ... found" instead of []. Only that exact
            // answer means empty; any other 404 is a failure.
            if (response.StatusCode == HttpStatusCode.NotFound
                && await IsEmptyResultAsync(response, cancellationToken))
            {
                return [];
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new DolibarrApiException(
                    $"Dolibarr returned HTTP {(int)response.StatusCode} for {resource}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<List<T>>(cancellationToken)
                    ?? throw new DolibarrApiException($"Dolibarr returned no {resource}.");
            }
            catch (JsonException exception)
            {
                throw new DolibarrApiException(
                    $"Dolibarr returned {resource} that could not be read.",
                    exception);
            }
        }
    }
}
