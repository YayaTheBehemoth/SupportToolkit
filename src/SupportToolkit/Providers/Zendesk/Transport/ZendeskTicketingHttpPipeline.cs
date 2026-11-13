namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// The only place the Zendesk ticketing HTTP pipeline is assembled.
///
/// Authenticated requests never follow redirects automatically. The
/// deletion connection has its own pipeline
/// (ZendeskDeletionHttpPipeline); the two share nothing.
/// </summary>
public static class ZendeskTicketingHttpPipeline
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
