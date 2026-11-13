using System.Net;

namespace SupportToolkit.Providers.MicrosoftGraph.Transport;

/// <summary>
/// The only place the development Graph HTTP pipeline is assembled.
///
/// It sends every request through the local Dev Proxy, which serves the
/// synthetic responses. There is no authentication: this is not a
/// production Graph pipeline.
/// </summary>
public static class GraphDevProxyHttpPipeline
{
    private const string DevProxyAddress =
        "http://127.0.0.1:8000";

    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(15);

    /// <summary>
    /// The client owns its handler; disposing the client disposes both.
    /// </summary>
    public static HttpClient Create()
    {
        var handler =
            new HttpClientHandler
            {
                UseProxy = true,
                Proxy = new WebProxy(DevProxyAddress),
                UseDefaultCredentials = false,
                UseCookies = false
            };

        return new HttpClient(handler)
        {
            Timeout = RequestTimeout
        };
    }
}
