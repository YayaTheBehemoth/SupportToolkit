
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// The narrow HTTP transport for the Zendesk ticketing connection.
/// Authentication is supplied by an access token source.
///
/// The current write surface intentionally permits only ticket creation.
/// </summary>
public sealed class ZendeskApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ZendeskOptions _options;
    private readonly IZendeskAccessTokenSource _tokenSource;
    private readonly OperationalLogger? _logger;

    /// <summary>
    /// Creates a client that authenticates with the client credentials
    /// grant, as the ticketing connection does.
    /// </summary>
    public ZendeskApiClient(
        HttpClient httpClient,
        ZendeskOptions options,
        TimeProvider? timeProvider = null,
        OperationalLogger? logger = null)
        : this(
            httpClient,
            options,
            new ZendeskClientCredentialsTokenSource(
                httpClient,
                options,
                timeProvider,
                logger),
            logger)
    {
    }

    public ZendeskApiClient(
        HttpClient httpClient,
        ZendeskOptions options,
        IZendeskAccessTokenSource tokenSource,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _tokenSource =
            tokenSource ?? throw new ArgumentNullException(nameof(tokenSource));

        _logger = logger;
    }

    /// <summary>
    /// Build a user-facing ticket URL using the configured Zendesk instance,
    /// rather than trusting a URL returned by an external response.
    /// </summary>
    public Uri GetTicketViewUri(long ticketId)
    {
        if (ticketId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ticketId),
                "A positive Zendesk ticket ID is required.");
        }

        return new Uri(
            _options.BaseUri,
            $"agent/tickets/{ticketId}");
    }

    /// <summary>
    /// Sends a ticket creation request with its idempotency key.
    /// No automatic POST retries are performed.
    /// </summary>
    public async Task<HttpResponseMessage> PostTicketAsync<TPayload>(
        TPayload payload,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        where TPayload : class
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "A Zendesk ticket creation requires an idempotency key.",
                nameof(idempotencyKey));
        }

        var accessToken =
            await _tokenSource.GetAccessTokenAsync(cancellationToken);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                BuildUri("/api/v2/tickets.json"));

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Add(
            "Idempotency-Key",
            idempotencyKey);

        request.Content = JsonContent.Create(payload);

        _logger?.Info("POST /api/v2/tickets.json");

        var stopwatch = Stopwatch.StartNew();

        var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        stopwatch.Stop();

        _logger?.Info(
            $"POST /api/v2/tickets.json -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)");

        return response;
    }

    private Uri BuildUri(string path)
    {
        return new Uri(
            _options.BaseUri,
            path.TrimStart('/'));
    }
}
