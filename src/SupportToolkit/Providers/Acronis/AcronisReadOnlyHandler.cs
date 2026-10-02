namespace SupportToolkit.Providers.Acronis;

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
                "Acronis request does not contain a URI."
            );

        ValidateOrigin(uri);

        var path =
            uri.AbsolutePath;

        if (IsAllowedTokenRequest(
                request.Method,
                path))
        {
            return;
        }

        if (request.Method == HttpMethod.Get
            && IsAllowedReadEndpoint(path))
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
        if (uri.Scheme != Uri.UriSchemeHttps)
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
            uri.Port == _allowedOrigin.Port;

        if (!sameHost || !samePort)
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

        return IsApiClientMetadataEndpoint(path);
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
                StringComparison.Ordinal)
            && string.Equals(
                segments[1],
                "2",
                StringComparison.Ordinal)
            && string.Equals(
                segments[2],
                "clients",
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(
                segments[3]
            );
    }

    private static InvalidOperationException Blocked(
        HttpRequestMessage request,
        string reason)
    {
        return new InvalidOperationException(
            $"Blocked by SupportToolkit read-only Acronis transport: " +
            $"{request.Method} " +
            $"{request.RequestUri?.AbsolutePath}. " +
            reason
        );
    }
}