
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Zendesk.Transport.Dtos;

namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// Owns Zendesk authentication and the narrow HTTP transport required by
/// SupportToolkit.
///
/// The current write surface intentionally permits only ticket creation.
/// </summary>
public sealed class ZendeskApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ZendeskOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly OperationalLogger? _logger;

    private readonly SemaphoreSlim _tokenRefreshGate = new(1, 1);

    private CachedAccessToken? _cachedAccessToken;

    public ZendeskApiClient(
        HttpClient httpClient,
        ZendeskOptions options,
        TimeProvider? timeProvider = null,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _timeProvider = timeProvider ?? TimeProvider.System;
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
            await GetAccessTokenAsync(cancellationToken);

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

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var cachedToken = _cachedAccessToken;

        if (IsUsable(cachedToken, now))
        {
            _logger?.Debug(
                "Using cached Zendesk OAuth access token.");

            return cachedToken!.AccessToken;
        }

        await _tokenRefreshGate.WaitAsync(cancellationToken);

        try
        {
            now = _timeProvider.GetUtcNow();
            cachedToken = _cachedAccessToken;

            if (IsUsable(cachedToken, now))
            {
                _logger?.Debug(
                    "Using cached Zendesk OAuth access token.");

                return cachedToken!.AccessToken;
            }

            _logger?.Info("Authenticating with Zendesk.");

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    BuildUri("/oauth/tokens"));

            request.Content =
                JsonContent.Create(
                    new ZendeskClientCredentialsRequestDto
                    {
                        ClientId = _options.ClientId,
                        ClientSecret = _options.ClientSecret
                    });

            var stopwatch = Stopwatch.StartNew();

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            stopwatch.Stop();

            _logger?.Info(
                $"POST /oauth/tokens -> " +
                $"{(int)response.StatusCode} " +
                $"({stopwatch.ElapsedMilliseconds} ms)");

            response.EnsureSuccessStatusCode();

            var token =
                await response.Content
                    .ReadFromJsonAsync<ZendeskOAuthTokenDto>(
                        cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Zendesk returned an empty OAuth token response.");

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException(
                    "Zendesk returned an OAuth response without an access token.");
            }

            if (!string.Equals(
                    token.TokenType,
                    "bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Zendesk returned an unexpected OAuth token type.");
            }

            if (token.ExpiresIn <= 0)
            {
                throw new InvalidOperationException(
                    "Zendesk returned an invalid OAuth token lifetime.");
            }

            cachedToken =
                new CachedAccessToken(
                    token.AccessToken,
                    now.AddSeconds(token.ExpiresIn));

            _cachedAccessToken = cachedToken;

            _logger?.Info("Zendesk authentication succeeded.");

            return cachedToken.AccessToken;
        }
        finally
        {
            _tokenRefreshGate.Release();
        }
    }

    private static bool IsUsable(
        CachedAccessToken? token,
        DateTimeOffset now)
    {
        return token is not null
            && now < token.ExpiresAt.AddMinutes(-1);
    }

    private Uri BuildUri(string path)
    {
        return new Uri(
            _options.BaseUri,
            path.TrimStart('/'));
    }

    private sealed record CachedAccessToken(
        string AccessToken,
        DateTimeOffset ExpiresAt);
}
