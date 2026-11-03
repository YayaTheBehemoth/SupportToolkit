using System.Text.RegularExpressions;

namespace SupportToolkit.Providers.Zendesk.Deletion.Transport;

/// <summary>
/// Enforces the HTTP boundary of the ticket deletion pipeline.
///
/// This is the only handler that permits a write, and it permits exactly
/// two things on the configured Zendesk HTTPS origin:
///
///   DELETE /api/v2/tickets/destroy_many(.json)?ids=1,2,3
///       with 1 to 100 distinct positive numeric ticket IDs and no other
///       query parameter, and
///   GET /api/v2/job_statuses/{id}.json
///       to follow the job that deletion started.
///
/// Everything else, including search and every other method, path or
/// host, is blocked before a token is requested. It is a different
/// pipeline from the read-only preview pipeline, which can never delete.
/// </summary>
public sealed class ZendeskTicketDeleteHandler
    : DelegatingHandler
{
    /// <summary>Zendesk accepts at most this many IDs per request.</summary>
    public const int MaxIdsPerRequest = 100;

    private static readonly HashSet<string> DeletePaths =
        new(StringComparer.Ordinal)
        {
            "/api/v2/tickets/destroy_many.json",
            "/api/v2/tickets/destroy_many"
        };

    private static readonly Regex JobStatusPath =
        new(
            @"^/api/v2/job_statuses/[A-Za-z0-9_-]{1,64}\.json$",
            RegexOptions.CultureInvariant);

    private readonly Uri _baseUri;

    public ZendeskTicketDeleteHandler(
        ZendeskDeletionOptions options,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(options);

        _baseUri = options.BaseUri;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        return base.SendAsync(
            request,
            cancellationToken);
    }

    private void ValidateRequest(
        HttpRequestMessage request)
    {
        var uri =
            request.RequestUri
            ?? throw new InvalidOperationException(
                "Zendesk deletion request has no URI.");

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw Blocked("HTTPS is required.");
        }

        if (!string.Equals(
                uri.Host,
                _baseUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != _baseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw Blocked("unexpected origin.");
        }

        if (request.Content is not null)
        {
            throw Blocked("the request cannot contain a body.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            throw Blocked("endpoint is not allowlisted.");
        }

        if (request.Method == HttpMethod.Delete)
        {
            if (!DeletePaths.Contains(uri.AbsolutePath))
            {
                throw Blocked("endpoint is not allowlisted.");
            }

            ValidateIds(uri.Query);

            return;
        }

        if (request.Method == HttpMethod.Get)
        {
            if (!JobStatusPath.IsMatch(uri.AbsolutePath)
                || !string.IsNullOrEmpty(uri.Query))
            {
                throw Blocked("endpoint is not allowlisted.");
            }

            return;
        }

        throw Blocked("only DELETE destroy_many and GET job status " +
                      "are allowed.");
    }

    /// <summary>
    /// The query must be exactly ?ids=a,b,c: 1 to 100 distinct positive
    /// decimal IDs, and nothing else.
    /// </summary>
    private static void ValidateIds(
        string query)
    {
        const string Prefix = "?ids=";

        if (!query.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw Blocked("the ids parameter is required.");
        }

        var ids =
            query[Prefix.Length..]
                .Split(',');

        if (ids.Length is < 1 or > MaxIdsPerRequest)
        {
            throw Blocked(
                $"between 1 and {MaxIdsPerRequest} ticket IDs are required.");
        }

        var seen = new HashSet<long>();

        foreach (var id in ids)
        {
            if (id.Length is 0 or > 18
                || !id.All(char.IsAsciiDigit)
                || !long.TryParse(id, out var value)
                || value <= 0
                || !seen.Add(value))
            {
                throw Blocked(
                    "ticket IDs must be distinct positive numbers.");
            }
        }
    }

    private static InvalidOperationException Blocked(
        string reason) =>
        new("Blocked Zendesk deletion request: " + reason);
}
