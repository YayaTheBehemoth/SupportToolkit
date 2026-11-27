namespace SupportToolkit.Providers.Zammad.Transport;

/// <summary>
/// The only place the Zammad HTTP pipeline is assembled.
///
/// Requests never follow redirects automatically: the API token is a
/// header and must not be replayed to wherever a redirect points.
/// </summary>
public static class ZammadHttpPipeline
{
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(30);

    public static HttpClient Create(
        HttpMessageHandler? networkHandler = null)
    {
        networkHandler ??=
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        return new HttpClient(networkHandler)
        {
            Timeout = RequestTimeout
        };
    }
}
