using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

public sealed class AcronisApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AcronisOptions _options;
    private readonly TimeProvider _timeProvider;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

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

        if (_accessToken is not null
            && now < _accessTokenExpiresAt.AddMinutes(-1))
        {
            return _accessToken;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri("/api/2/idp/token")
        );

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