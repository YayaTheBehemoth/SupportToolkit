using System.Net;

namespace SupportToolkit.Providers.Dolibarr.Transport;

/// <summary>
/// The only place the Dolibarr HTTP pipeline is assembled.
///
/// It is read-only by construction: DolibarrReadOnlyHandler lets through
/// only GET requests to the REST API, so a mistake elsewhere cannot change
/// a customer or a contract. Redirects are not followed, so the API key is
/// never replayed elsewhere.
/// </summary>
public static class DolibarrHttpPipeline
{
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(60);

    public static HttpClient Create(
        HttpMessageHandler? networkHandler = null)
    {
        networkHandler ??=
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        return new HttpClient(
            new DolibarrReadOnlyHandler { InnerHandler = networkHandler })
        {
            Timeout = RequestTimeout
        };
    }
}

/// <summary>Refuses every request but REST reads, before it is sent.</summary>
public sealed class DolibarrReadOnlyHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method != HttpMethod.Get
            || !path.Contains("/api/index.php/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The Dolibarr connection is read-only; {request.Method} " +
                $"{path} was refused before it was sent.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Validates a Dolibarr address. HTTPS is required because the API key
/// travels in a header; plain HTTP is accepted only for a loopback host.
/// </summary>
public static class DolibarrOptions
{
    public static Uri ParseApiRoot(string baseUrl, string connectionName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Dolibarr connection '{connectionName}' needs an absolute " +
                "baseUrl, for example https://erp.example.com/.");
        }

        var isLoopback =
            uri.IsLoopback
            || (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));

        if (uri.Scheme != Uri.UriSchemeHttps
            && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            throw new InvalidOperationException(
                $"Dolibarr connection '{connectionName}' must use HTTPS. " +
                "Plain HTTP is accepted only for a loopback address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"Dolibarr connection '{connectionName}' baseUrl must not " +
                "contain credentials, a query or a fragment.");
        }

        var root =
            uri.AbsolutePath.EndsWith('/')
                ? uri
                : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");

        return new Uri(root, "api/index.php/");
    }
}
