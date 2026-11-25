using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Duplicati.Transport.Dtos;

namespace SupportToolkit.Providers.Duplicati.Transport;

/// <summary>
/// Reads one Duplicati server: signs in with its password, then reads its
/// jobs, their run logs and the server state. Nothing else is possible
/// through the pipeline (DuplicatiReadOnlyHandler).
/// </summary>
public sealed class DuplicatiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly OperationalLogger? _logger;
    private string? _accessToken;

    public DuplicatiApiClient(
        HttpClient httpClient,
        Uri baseUri,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _baseUri =
            baseUri ?? throw new ArgumentNullException(nameof(baseUri));

        _logger = logger;
    }

    public async Task SignInAsync(
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_baseUri, DuplicatiReadOnlyHandler.SignInPath))
            {
                Content = JsonContent.Create(
                    new DuplicatiLoginRequestDto { Password = password, RememberMe = false })
            };

        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "Duplicati refused the password (HTTP 401).");
        }

        EnsureOk(response);

        var login =
            await ReadJsonAsync<DuplicatiLoginResponseDto>(response, cancellationToken);

        if (string.IsNullOrWhiteSpace(login?.AccessToken))
        {
            throw new InvalidOperationException(
                "Duplicati accepted the sign-in but returned no token.");
        }

        _accessToken = login.AccessToken;
    }

    internal Task<IReadOnlyList<DuplicatiBackupEntryDto>> GetBackupsAsync(
        CancellationToken cancellationToken) =>
        GetListAsync<DuplicatiBackupEntryDto>("api/v1/backups", cancellationToken);

    internal Task<IReadOnlyList<DuplicatiLogEntryDto>> GetLogAsync(
        string backupId,
        int pageSize,
        CancellationToken cancellationToken) =>
        GetListAsync<DuplicatiLogEntryDto>(
            $"api/v1/backup/{Uri.EscapeDataString(backupId)}/log?pagesize={pageSize}",
            cancellationToken);

    internal async Task<DuplicatiServerStateDto> GetServerStateAsync(
        CancellationToken cancellationToken) =>
        await GetAsync<DuplicatiServerStateDto>("api/v1/serverstate", cancellationToken)
        ?? throw new InvalidOperationException("Duplicati returned no server state.");

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        string path,
        CancellationToken cancellationToken) =>
        await GetAsync<List<T>>(path, cancellationToken)
        ?? throw new InvalidOperationException("Duplicati returned no list.");

    private async Task<T?> GetAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        if (_accessToken is null)
        {
            throw new InvalidOperationException(
                "Sign in to the Duplicati server before reading it.");
        }

        using var request =
            new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, path));

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _accessToken);

        using var response = await SendAsync(request, cancellationToken);

        EnsureOk(response);

        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Path only: no query, no host, nothing secret.
        var path = request.RequestUri!.AbsolutePath;

        _logger?.Debug($"{request.Method} {path}");

        var response = await _httpClient.SendAsync(request, cancellationToken);

        _logger?.Debug($"{request.Method} {path} -> {(int)response.StatusCode}");

        return response;
    }

    private static void EnsureOk(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException(
                $"Duplicati returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }
    }

    private static async Task<T?> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Duplicati returned a response that could not be read.",
                exception);
        }
    }
}
