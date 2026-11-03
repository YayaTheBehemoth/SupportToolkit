using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// Read-only JSON GET client for Business Central.
///
/// Response bodies of failed requests are never read, so error text
/// cannot reach logs or operator output.
/// </summary>
public sealed class BusinessCentralApiClient
{
    private readonly HttpClient _httpClient;
    private readonly OperationalLogger? _logger;

    public BusinessCentralApiClient(
        HttpClient httpClient,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient
            ?? throw new ArgumentNullException(
                nameof(httpClient)
            );

        if (httpClient.BaseAddress is null)
        {
            throw new ArgumentException(
                "The Business Central HTTP client requires a " +
                "base address.",
                nameof(httpClient)
            );
        }

        _logger = logger;
    }

    public Uri ApiBaseUri =>
        _httpClient.BaseAddress!;

    public async Task<T> GetJsonAsync<T>(
        string relativePath,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            relativePath
        );

        // The query string is omitted from logs.
        var logPath =
            relativePath.Split('?')[0];

        _logger?.Info(
            $"GET {logPath}"
        );

        var stopwatch =
            Stopwatch.StartNew();

        using var response =
            await _httpClient.GetAsync(
                relativePath,
                cancellationToken
            );

        stopwatch.Stop();

        _logger?.Info(
            $"GET {logPath} -> " +
            $"{(int)response.StatusCode} " +
            $"({stopwatch.ElapsedMilliseconds} ms)"
        );

        EnsureSuccess(response);

        try
        {
            return await response.Content
                .ReadFromJsonAsync<T>(
                    cancellationToken: cancellationToken
                )
                ?? throw new BusinessCentralContractException(
                    "Business Central returned an empty response."
                );
        }
        catch (JsonException exception)
        {
            throw new BusinessCentralContractException(
                "Business Central returned data that did not " +
                "match the expected contract.",
                exception
            );
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response)
    {
        var status =
            response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var code =
            (int)status;

        var message =
            status switch
            {
                HttpStatusCode.Unauthorized =>
                    "Business Central rejected the access token. " +
                    "Run the command again to sign in.",

                HttpStatusCode.Forbidden =>
                    "Business Central denied access. The signed-in " +
                    "account lacks permission for the required " +
                    "API pages.",

                HttpStatusCode.NotFound =>
                    "Business Central did not find the requested " +
                    "API page, company or environment. Check the " +
                    "configuration.",

                HttpStatusCode.TooManyRequests =>
                    DescribeThrottle(response),

                _ when code >= 500 =>
                    $"Business Central reported a server error " +
                    $"(HTTP {code}).",

                _ =>
                    $"Business Central returned an unexpected " +
                    $"response (HTTP {code})."
            };

        throw new BusinessCentralApiException(
            status,
            message
        );
    }

    private static string DescribeThrottle(
        HttpResponseMessage response)
    {
        var delay =
            response.Headers.RetryAfter?.Delta;

        return delay is { } wait
            ? "Business Central throttled the request. Try " +
              $"again in about {(int)Math.Ceiling(wait.TotalSeconds)} " +
              "seconds."
            : "Business Central throttled the request. " +
              "Try again later.";
    }
}
