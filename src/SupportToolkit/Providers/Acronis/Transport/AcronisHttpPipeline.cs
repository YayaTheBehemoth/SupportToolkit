namespace SupportToolkit.Providers.Acronis.Transport;

/// <summary>
/// The only place the Acronis HTTP pipeline is assembled.
///
/// Every request passes the read-only handler, which allows only the
/// approved read requests to the configured datacenter. Redirects are
/// disabled so a token is never replayed to another location.
/// </summary>
public static class AcronisHttpPipeline
{
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(30);

    public static HttpClient Create(
        AcronisOptions options,
        HttpMessageHandler? networkHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        networkHandler ??=
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        return new HttpClient(
            new AcronisReadOnlyHandler(
                options.DatacenterUrl,
                networkHandler))
        {
            Timeout = RequestTimeout
        };
    }
}
