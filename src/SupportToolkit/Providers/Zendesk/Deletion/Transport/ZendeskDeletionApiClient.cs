using System.Net;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.Zendesk.Deletion.Transport;

/// <summary>
/// A Zendesk request failed in a way the operator can act on. The
/// message never contains the response body.
/// </summary>
public sealed class ZendeskApiException : Exception
{
    public ZendeskApiException(
        HttpStatusCode statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>
/// JSON transport for the Zendesk deletion connection.
///
/// What it may send is decided by the pipeline it is built on: the
/// read-only pipeline allows only the allowlisted GET endpoints, and the
/// deletion pipeline only ticket deletion and its job status. It is a
/// different client from the ticket-creation ZendeskApiClient, which it
/// never uses.
/// </summary>
public sealed class ZendeskDeletionApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ZendeskDeletionOptions _options;
    private readonly OperationalLogger? _logger;

    public ZendeskDeletionApiClient(
        HttpClient httpClient,
        ZendeskDeletionOptions options,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _logger = logger;
    }

    public Task<T> GetJsonAsync<T>(
        string pathAndQuery,
        CancellationToken cancellationToken = default)
        where T : class =>
        SendJsonAsync<T>(
            HttpMethod.Get,
            pathAndQuery,
            cancellationToken);

    /// <summary>
    /// Sends a DELETE. There is no automatic retry: a deletion whose
    /// outcome is unclear is reported as unknown, never repeated.
    /// </summary>
    public Task<T> DeleteJsonAsync<T>(
        string pathAndQuery,
        CancellationToken cancellationToken = default)
        where T : class =>
        SendJsonAsync<T>(
            HttpMethod.Delete,
            pathAndQuery,
            cancellationToken);

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string pathAndQuery,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathAndQuery);

        var uri =
            new Uri(
                _options.BaseUri,
                pathAndQuery.TrimStart('/'));

        using var request =
            new HttpRequestMessage(method, uri);

        // Only the route is logged: queries can hold search terms or
        // ticket IDs.
        _logger?.Info($"{method.Method} {uri.AbsolutePath}");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        _logger?.Info(
            $"{method.Method} {uri.AbsolutePath} -> " +
            $"{(int)response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw Describe(response);
        }

        return await response.Content
                   .ReadFromJsonAsync<T>(
                       cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException(
                   "Zendesk returned an empty response.");
    }

    private static ZendeskApiException Describe(
        HttpResponseMessage response)
    {
        var status = response.StatusCode;

        var message =
            status switch
            {
                HttpStatusCode.Unauthorized =>
                    "Zendesk rejected the sign-in (401). Sign in again.",

                HttpStatusCode.Forbidden =>
                    "Zendesk denied access (403). The signed-in account " +
                    "needs permission for this action (searching or " +
                    "deleting tickets), and the OAuth client must allow " +
                    "the 'read' and 'tickets:write' scopes.",

                HttpStatusCode.TooManyRequests =>
                    "Zendesk rate limit reached (429)." +
                    RetryAfter(response),

                _ =>
                    "Zendesk request failed " +
                    $"(status {(int)status})."
            };

        return new ZendeskApiException(status, message);
    }

    private static string RetryAfter(
        HttpResponseMessage response)
    {
        var after = response.Headers.RetryAfter?.Delta;

        return after is null
            ? " Try again shortly."
            : $" Try again in {(int)Math.Ceiling(after.Value.TotalSeconds)} seconds.";
    }
}
