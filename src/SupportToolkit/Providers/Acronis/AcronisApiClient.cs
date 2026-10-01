using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;


/// Encapsulates the raw Acronis HTTP and authentication flow for the SupportToolkit integration.
/// This class isolates transport and credential concerns so consuming code can treat Acronis as a data source
/// rather than dealing with client-credentials authentication, bearer token handling, or network details.

public sealed class AcronisApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AcronisOptions _options;
    private readonly TimeProvider _timeProvider;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;


    /// Initializes a new Acronis API client.
    /// <param name="httpClient">The HTTP client used to send requests to Acronis.</param>
    /// <param name="options">The Acronis configuration options.</param>    
    /// <param name="timeProvider">An optional time provider for testing or fixture scenarios.</param>
    public AcronisApiClient(
        HttpClient httpClient,
        AcronisOptions options,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }


    public async Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        // Subsequent requests use the bearer token issued from the client-credentials flow rather than the client secret.
        var accessToken =
            await GetAccessTokenAsync(cancellationToken);

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(path)
        );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken
            );

        return await _httpClient.SendAsync(
            request,
            cancellationToken
        );
    }


    public async Task<string> GetRootTenantIdAsync(
        CancellationToken cancellationToken = default)
    {
        // The root tenant is resolved once at the API boundary so callers do not need to know the client-specific tenant hierarchy.
        using var response = await GetAsync(
            $"/api/2/clients/{Uri.EscapeDataString(_options.ClientId)}",
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var client = await response.Content
            .ReadFromJsonAsync<AcronisClientDto>(
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty API client response."
            );

        return client.TenantId;
    }

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        // Refresh before expiry to avoid reusing a token that may expire mid-request.
        // The one-minute safety margin keeps the cached token valid for a short buffer beyond the server's expiration.
        if (_accessToken is not null
            && now < _accessTokenExpiresAt.AddMinutes(-1))
        {
            return _accessToken;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri("/api/2/idp/token")
        );

        // The client-credentials flow authenticates the application itself, not a user, so Acronis expects Basic auth with the client id and secret.
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"
            )
        );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                credentials
            );

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            }
        );

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken
        );

        response.EnsureSuccessStatusCode();

        var token = await response.Content
            .ReadFromJsonAsync<AcronisTokenDto>(
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty token response."
            );

        _accessToken = token.AccessToken;

        _accessTokenExpiresAt =
            DateTimeOffset.FromUnixTimeSeconds(
                token.ExpiresOn
            );

        return _accessToken;
    }

    private Uri BuildUri(string path)
    {
        var baseUrl =
            _options.DatacenterUrl.TrimEnd('/');

        var normalizedPath =
            path.StartsWith('/')
                ? path
                : $"/{path}";

        return new Uri(
            $"{baseUrl}{normalizedPath}"
        );
    }
}