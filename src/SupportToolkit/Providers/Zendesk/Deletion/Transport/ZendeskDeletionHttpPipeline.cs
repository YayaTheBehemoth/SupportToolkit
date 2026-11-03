using SupportToolkit.Providers.Zendesk.Deletion.Auth;

namespace SupportToolkit.Providers.Zendesk.Deletion.Transport;

/// <summary>
/// The only place the Zendesk deletion HTTP pipelines are assembled.
///
/// There are two, and they are never the same client:
///   Create:         read-only search and lookups (the preview).
///   CreateDeleter:  ticket deletion and its job status only.
///
/// Order in each: allowlist validation, then bearer token, then network.
/// Redirects are disabled so a token is never replayed elsewhere, and
/// cookies are not kept.
/// </summary>
public static class ZendeskDeletionHttpPipeline
{
    private const int MaxResponseBytes =
        10 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(60);

    public static HttpClient Create(
        ZendeskDeletionOptions options,
        IZendeskInvalidatableTokenSource tokenSource,
        HttpMessageHandler? networkHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenSource);

        return Build(
            options,
            tokenSource,
            networkHandler,
            bearer => new ZendeskDeletionReadOnlyHandler(options, bearer));
    }

    /// <summary>
    /// The pipeline that may delete tickets. Only the deletion workflow
    /// builds it, and only when deletion was explicitly requested.
    /// </summary>
    public static HttpClient CreateDeleter(
        ZendeskDeletionOptions options,
        IZendeskInvalidatableTokenSource tokenSource,
        HttpMessageHandler? networkHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenSource);

        return Build(
            options,
            tokenSource,
            networkHandler,
            bearer => new ZendeskTicketDeleteHandler(options, bearer));
    }

    /// <summary>
    /// A client for the sign-in token exchange, which is not an API
    /// request and carries no bearer token.
    /// </summary>
    public static HttpClient CreateSignInClient(
        ZendeskDeletionOptions options,
        HttpMessageHandler? networkHandler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new HttpClient(networkHandler ?? CreateNetworkHandler())
        {
            Timeout = RequestTimeout
        };
    }

    private static HttpClient Build(
        ZendeskDeletionOptions options,
        IZendeskInvalidatableTokenSource tokenSource,
        HttpMessageHandler? networkHandler,
        Func<HttpMessageHandler, HttpMessageHandler> allowlist)
    {
        networkHandler ??=
            CreateNetworkHandler();

        var bearer =
            new ZendeskDeletionBearerHandler(
                tokenSource,
                networkHandler);

        return new HttpClient(allowlist(bearer))
        {
            BaseAddress = options.BaseUri,
            Timeout = RequestTimeout,
            MaxResponseContentBufferSize = MaxResponseBytes
        };
    }

    private static HttpClientHandler CreateNetworkHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };
}
