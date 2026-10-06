using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Encapsulates the raw Acronis HTTP and authentication flow for the
/// SupportToolkit integration.
/// </summary>
public sealed class AcronisApiClient
{
    private readonly HttpClient _httpClient;
    private readonly AcronisOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly OperationalLogger? _logger;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;
    private string? _rootTenantId;

    public AcronisApiClient(
        HttpClient httpClient,
        AcronisOptions options,
        TimeProvider? timeProvider = null,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient;

        _options =
            options;

        _timeProvider =
            timeProvider
            ?? TimeProvider.System;

        _logger =
            logger;
    }

    public async Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var accessToken =
            await GetAccessTokenAsync(
                cancellationToken
            );

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                BuildUri(path)
            );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken
            );

        var safePath =
            GetSafeLogPath(
                request.RequestUri
            );

        _logger?.Info(
            $"GET {safePath}"
        );

        var stopwatch =
            Stopwatch.StartNew();

        var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken
            );

        stopwatch.Stop();

        _logger?.Info(
            $"GET {safePath} -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)"
        );

        return response;
    }

    public async Task<string> GetRootTenantIdAsync(
        CancellationToken cancellationToken = default)
    {
        if (_rootTenantId is not null)
        {
            _logger?.Debug(
                "Using cached Acronis root tenant ID."
            );

            return _rootTenantId;
        }

        _logger?.Info(
            "Resolving Acronis root tenant."
        );

        using var response =
            await GetAsync(
                $"/api/2/clients/" +
                $"{Uri.EscapeDataString(_options.ClientId)}",
                cancellationToken
            );

        response.EnsureSuccessStatusCode();

        var client =
            await response.Content
                .ReadFromJsonAsync<AcronisClientDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty API client response."
            );

        _rootTenantId =
            client.TenantId;

        _logger?.Info(
            "Acronis root tenant resolved."
        );

        return _rootTenantId;
    }

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        var now =
            _timeProvider.GetUtcNow();

        if (_accessToken is not null
            && now
            < _accessTokenExpiresAt.AddMinutes(-1))
        {
            _logger?.Debug(
                "Using cached Acronis access token."
            );

            return _accessToken;
        }

        _logger?.Info(
            "Authenticating with Acronis."
        );

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                BuildUri(
                    "/api/2/idp/token"
                )
            );

        var credentials =
            Convert.ToBase64String(
                Encoding.ASCII.GetBytes(
                    $"{_options.ClientId}:" +
                    $"{_options.ClientSecret}"
                )
            );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                credentials
            );

        request.Content =
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] =
                        "client_credentials"
                }
            );

        var stopwatch =
            Stopwatch.StartNew();

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken
            );

        stopwatch.Stop();

        _logger?.Info(
            $"POST /api/2/idp/token -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)"
        );

        response.EnsureSuccessStatusCode();

        var token =
            await response.Content
                .ReadFromJsonAsync<AcronisTokenDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty token response."
            );

        _accessToken =
            token.AccessToken;

        _accessTokenExpiresAt =
            DateTimeOffset.FromUnixTimeSeconds(
                token.ExpiresOn
            );

        _logger?.Info(
            "Acronis authentication succeeded."
        );

        return _accessToken;
    }

    private Uri BuildUri(
        string path)
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

    private static string GetSafeLogPath(
        Uri? uri)
    {
        if (uri is null)
        {
            return "<unknown>";
        }

        var path =
            uri.AbsolutePath;

        var segments =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

        if (segments.Length == 4
            && segments[0] == "api"
            && segments[1] == "2"
            && segments[2] == "clients")
        {
            return "/api/2/clients/{client_id}";
        }

        if (segments.Length == 4
            && segments[0] == "api"
            && segments[1] == "1"
            && segments[2] == "groups")
        {
            return "/api/1/groups/{tenant_id}";
        }

        if (segments.Length == 5
            && segments[0] == "api"
            && segments[1] == "1"
            && segments[2] == "groups"
            && segments[4] == "children")
        {
            return "/api/1/groups/{tenant_id}/children";
        }

        return path;
    }
}