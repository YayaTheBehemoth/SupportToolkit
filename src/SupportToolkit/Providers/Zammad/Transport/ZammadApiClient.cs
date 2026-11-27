using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.Zammad.Transport;

/// <summary>
/// The HTTP transport for a Zammad instance. Every request carries the API
/// token as "Authorization: Token token=…"; nothing logged ever includes
/// it.
/// </summary>
public sealed class ZammadApiClient
{
    private const string TicketsPath = "api/v1/tickets";

    private readonly HttpClient _httpClient;
    private readonly ZammadOptions _options;
    private readonly OperationalLogger? _logger;

    public ZammadApiClient(
        HttpClient httpClient,
        ZammadOptions options,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _logger = logger;
    }

    public Uri BaseUri => _options.BaseUri;

    /// <summary>
    /// The agent view of a ticket, built from the configured instance
    /// rather than from anything Zammad returned.
    /// </summary>
    public Uri GetTicketViewUri(long ticketId)
    {
        if (ticketId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticketId),
                ticketId,
                "A Zammad ticket ID is positive.");
        }

        return new Uri(
            _options.BaseUri,
            "#ticket/zoom/" + ticketId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Sends one ticket creation request. Never retried: Zammad has no
    /// idempotency, so a repeat would create another ticket.
    /// </summary>
    public async Task<HttpResponseMessage> PostTicketAsync<TPayload>(
        TPayload payload,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.BaseUri, TicketsPath))
            {
                Content = JsonContent.Create(payload)
            };

        return await SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// One read: a path below api/v1/ with its query. The query is never
    /// logged (it may hold search terms such as an email address).
    /// </summary>
    public async Task<HttpResponseMessage> GetAsync(
        string pathAndQuery,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathAndQuery);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(_options.BaseUri, "api/v1/" + pathAndQuery.TrimStart('/')));

        return await SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Deletes one ticket by its ID. Zammad deletes at once and
    /// permanently. Never retried.
    /// </summary>
    public async Task<HttpResponseMessage> DeleteTicketAsync(
        long ticketId,
        CancellationToken cancellationToken = default)
    {
        if (ticketId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticketId),
                ticketId,
                "A Zammad ticket ID is positive.");
        }

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(
                    _options.BaseUri,
                    TicketsPath + "/" + ticketId.ToString(CultureInfo.InvariantCulture)));

        return await SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            "Token token=" + _options.ApiToken);

        var path = request.RequestUri!.AbsolutePath;

        _logger?.Info($"{request.Method} {path}");

        var stopwatch = Stopwatch.StartNew();

        var response =
            await _httpClient.SendAsync(request, cancellationToken);

        stopwatch.Stop();

        _logger?.Info(
            $"{request.Method} {path} -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)");

        return response;
    }
}
