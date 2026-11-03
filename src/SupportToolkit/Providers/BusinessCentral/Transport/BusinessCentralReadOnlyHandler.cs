
namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// Enforces the read-only Business Central HTTP boundary.
///
/// Only explicitly allowlisted GET paths on the configured HTTPS
/// API origin are permitted.
///
/// No production endpoints are approved by default.
/// Authentication is intentionally outside this handler.
/// </summary>
public sealed class BusinessCentralReadOnlyHandler
    : DelegatingHandler
{
    private readonly Uri _apiBaseUri;

    private readonly HashSet<string> _allowedGetPaths =
        new(StringComparer.Ordinal);

    public BusinessCentralReadOnlyHandler(
        string apiBaseUrl,
        HttpMessageHandler innerHandler,
        IEnumerable<string>? approvedRelativePaths = null)
        : base(innerHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            apiBaseUrl
        );

        var normalizedUrl =
            apiBaseUrl.TrimEnd('/') + "/";

        _apiBaseUri =
            new Uri(
                normalizedUrl,
                UriKind.Absolute
            );

        if (_apiBaseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(_apiBaseUri.Query)
            || !string.IsNullOrEmpty(_apiBaseUri.Fragment)
            || !string.IsNullOrEmpty(_apiBaseUri.UserInfo))
        {
            throw new InvalidOperationException(
                "Business Central transport requires a clean HTTPS API base URL."
            );
        }

        foreach (var path in approvedRelativePaths
            ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith('/')
                || path.Contains("..", StringComparison.Ordinal)
                || path.Contains('?')
                || path.Contains('#'))
            {
                throw new ArgumentException(
                    "Approved Business Central paths must be " +
                    "relative paths without query strings or traversal.",
                    nameof(approvedRelativePaths)
                );
            }

            var approvedUri =
                new Uri(
                    _apiBaseUri,
                    path
                );

            if (!approvedUri.AbsolutePath.StartsWith(
                    _apiBaseUri.AbsolutePath,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Approved path escapes the configured API base path.",
                    nameof(approvedRelativePaths)
                );
            }

            _allowedGetPaths.Add(
                approvedUri.AbsolutePath
            );
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        return base.SendAsync(
            request,
            cancellationToken
        );
    }

    private void ValidateRequest(
        HttpRequestMessage request)
    {
        var uri =
            request.RequestUri
            ?? throw new InvalidOperationException(
                "Business Central request has no URI."
            );

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Blocked Business Central request: HTTPS is required."
            );
        }

        if (!string.Equals(
                uri.Host,
                _apiBaseUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != _apiBaseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                "Blocked Business Central request: unexpected API origin."
            );
        }

        if (request.Method != HttpMethod.Get)
        {
            throw new InvalidOperationException(
                "Blocked Business Central request: only GET is allowed."
            );
        }

        if (request.Content is not null)
        {
            throw new InvalidOperationException(
                "Blocked Business Central request: GET cannot contain a body."
            );
        }

        if (!string.IsNullOrEmpty(uri.Fragment)
            || !_allowedGetPaths.Contains(uri.AbsolutePath))
        {
            throw new InvalidOperationException(
                "Blocked Business Central request: endpoint is not allowlisted."
            );
        }
    }
}
