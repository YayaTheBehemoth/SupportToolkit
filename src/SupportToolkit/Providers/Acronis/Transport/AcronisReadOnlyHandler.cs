namespace SupportToolkit.Providers.Acronis.Transport;

/// <summary>
/// Enforces SupportToolkit's read-only Acronis integration boundary.
///
/// Only explicitly approved read endpoints and the OAuth token request are
/// permitted. Every other method, endpoint, host, or protocol is rejected
/// before a network request is transmitted.
/// </summary>
public sealed class AcronisReadOnlyHandler : DelegatingHandler
{
    private readonly Uri _allowedOrigin;

    public AcronisReadOnlyHandler(
        string datacenterUrl,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        var normalizedBaseUrl =
            datacenterUrl.TrimEnd('/') + "/";

        _allowedOrigin =
            new Uri(
                normalizedBaseUrl,
                UriKind.Absolute
            );

        if (_allowedOrigin.Scheme
            != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Acronis read-only transport requires HTTPS."
            );
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(
            request
        );

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
                "Acronis request does not contain a URI."
            );

        ValidateOrigin(
            uri
        );

        var path =
            uri.AbsolutePath;

        if (IsAllowedTokenRequest(
                request.Method,
                path))
        {
            return;
        }

        if (request.Method == HttpMethod.Get
            && IsAllowedReadEndpoint(
                path))
        {
            if (request.Content is not null)
            {
                throw Blocked(
                    request,
                    "GET requests must not contain a request body."
                );
            }

            return;
        }

        throw Blocked(
            request,
            "The operation is not part of the SupportToolkit " +
            "read-only Acronis allowlist."
        );
    }

    private void ValidateOrigin(
        Uri uri)
    {
        if (uri.Scheme
            != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Blocked Acronis request: only HTTPS is permitted."
            );
        }

        var sameHost =
            string.Equals(
                uri.Host,
                _allowedOrigin.Host,
                StringComparison.OrdinalIgnoreCase
            );

        var samePort =
            uri.Port
            == _allowedOrigin.Port;

        if (!sameHost
            || !samePort)
        {
            throw new InvalidOperationException(
                $"Blocked Acronis request to unexpected origin " +
                $"'{uri.GetLeftPart(UriPartial.Authority)}'."
            );
        }
    }

    private static bool IsAllowedTokenRequest(
        HttpMethod method,
        string path)
    {
        return method == HttpMethod.Post
            && string.Equals(
                path,
                "/api/2/idp/token",
                StringComparison.Ordinal
            );
    }

    private static bool IsAllowedReadEndpoint(
        string path)
    {
        if (string.Equals(
                path,
                "/api/2/tenants",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                path,
                "/api/resource_management/v4/resource_statuses",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                path,
                "/api/alert_manager/v1/alerts",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                path,
                "/api/task_manager/v2/activities",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                path,
                "/bc/api/resource_manager/v1/o365/groups",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                path,
                "/bc/api/resource_manager/v1/epm/resources",
                StringComparison.Ordinal))
        {
            return true;
        }

        return IsApiClientMetadataEndpoint(
                   path
               )
               || IsLegacyTenantBootstrapEndpoint(
                   path
               )
               || IsLegacyTenantChildrenEndpoint(
                   path
               )
               || IsO365ResourceEndpoint(
                   path
               );
    }

    private static bool IsApiClientMetadataEndpoint(
        string path)
    {
        var segments =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

        return segments.Length == 4
            && string.Equals(
                segments[0],
                "api",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[1],
                "2",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[2],
                "clients",
                StringComparison.Ordinal
            )
            && !string.IsNullOrWhiteSpace(
                segments[3]
            );
    }

    private static bool IsLegacyTenantBootstrapEndpoint(
        string path)
    {
        var segments =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

        return segments.Length == 4
            && string.Equals(
                segments[0],
                "api",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[1],
                "1",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[2],
                "groups",
                StringComparison.Ordinal
            )
            && Guid.TryParse(
                segments[3],
                out _
            );
    }

    private static bool IsLegacyTenantChildrenEndpoint(
        string path)
    {
        var segments =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

        return segments.Length == 5
            && string.Equals(
                segments[0],
                "api",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[1],
                "1",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[2],
                "groups",
                StringComparison.Ordinal
            )
            && long.TryParse(
                segments[3],
                out _
            )
            && string.Equals(
                segments[4],
                "children",
                StringComparison.Ordinal
            );
    }

    private static bool IsO365ResourceEndpoint(
        string path)
    {
        var segments =
            path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries
            );

        return segments.Length == 8
            && string.Equals(
                segments[0],
                "bc",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[1],
                "api",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[2],
                "resource_manager",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[3],
                "v1",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[4],
                "o365",
                StringComparison.Ordinal
            )
            && string.Equals(
                segments[5],
                "groups",
                StringComparison.Ordinal
            )
            && Guid.TryParse(
                segments[6],
                out _
            )
            && string.Equals(
                segments[7],
                "resources",
                StringComparison.Ordinal
            );
    }

    private static InvalidOperationException Blocked(
        HttpRequestMessage request,
        string reason)
    {
        var method =
            request.Method.Method;

        var path =
            request.RequestUri?
                .AbsolutePath
            ?? "<unknown>";

        return new InvalidOperationException(
            $"Blocked Acronis request: " +
            $"{method} {path}. {reason}"
        );
    }
}