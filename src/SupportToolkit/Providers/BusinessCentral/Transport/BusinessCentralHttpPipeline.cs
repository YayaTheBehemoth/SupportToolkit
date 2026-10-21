using SupportToolkit.Providers.BusinessCentral.Auth;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// The only place the Business Central HTTP pipeline is assembled.
///
/// Order: read-only validation, then bearer token, then network.
/// Redirects are disabled so a token is never replayed to another
/// location.
/// </summary>
public static class BusinessCentralHttpPipeline
{
    private const int MaxResponseBytes =
        10 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(60);

    public static HttpClient Create(
        BusinessCentralOptions options,
        IBusinessCentralTokenProvider tokenProvider,
        IEnumerable<string> approvedRelativePaths,
        HttpMessageHandler? networkHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(approvedRelativePaths);

        networkHandler ??=
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };

        var bearer =
            new BusinessCentralBearerHandler(
                tokenProvider,
                networkHandler
            );

        var readOnly =
            new BusinessCentralReadOnlyHandler(
                options.ApiBaseUri.ToString(),
                bearer,
                approvedRelativePaths
            );

        var client =
            new HttpClient(readOnly)
            {
                BaseAddress =
                    options.ApiBaseUri,

                Timeout =
                    RequestTimeout,

                MaxResponseContentBufferSize =
                    MaxResponseBytes
            };

        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(
            BusinessCentralOptions.RequestLanguage
        );

        return client;
    }
}
