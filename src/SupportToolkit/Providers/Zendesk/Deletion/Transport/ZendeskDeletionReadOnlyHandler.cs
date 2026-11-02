namespace SupportToolkit.Providers.Zendesk.Deletion.Transport;

/// <summary>
/// Enforces the Zendesk deletion connection's HTTP boundary.
///
/// In this version only four read endpoints are allowed, as GET on the
/// configured Zendesk HTTPS origin: search, the signed-in user, and the
/// user and organization lookups the preview needs. Anything else, every
/// other method, and any other host is blocked before a token is
/// requested, so a blocked request never triggers sign-in.
///
/// Deleting tickets is deliberately not allowlisted yet.
/// </summary>
public sealed class ZendeskDeletionReadOnlyHandler
    : DelegatingHandler
{
    private static readonly HashSet<string> AllowedGetPaths =
        new(StringComparer.Ordinal)
        {
            "/api/v2/search.json",
            "/api/v2/users/me.json",
            "/api/v2/users/show_many.json",
            "/api/v2/organizations/show_many.json"
        };

    private readonly Uri _baseUri;

    public ZendeskDeletionReadOnlyHandler(
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
            throw new InvalidOperationException(
                "Blocked Zendesk deletion request: HTTPS is required.");
        }

        if (!string.Equals(
                uri.Host,
                _baseUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != _baseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "Blocked Zendesk deletion request: unexpected origin.");
        }

        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException(
                "Blocked Zendesk deletion request: only GET is allowed.");
        }

        if (request.Content is not null)
        {
            throw new InvalidOperationException(
                "Blocked Zendesk deletion request: GET cannot contain " +
                "a body.");
        }

        if (!string.IsNullOrEmpty(uri.Fragment)
            || !AllowedGetPaths.Contains(uri.AbsolutePath))
        {
            throw new InvalidOperationException(
                "Blocked Zendesk deletion request: endpoint is not " +
                "allowlisted.");
        }
    }
}
