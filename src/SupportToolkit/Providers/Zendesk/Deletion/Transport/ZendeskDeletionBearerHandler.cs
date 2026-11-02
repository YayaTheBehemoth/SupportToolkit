using System.Net;
using System.Net.Http.Headers;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;

namespace SupportToolkit.Providers.Zendesk.Deletion.Transport;

/// <summary>
/// Attaches the signed-in operator's bearer token to outgoing requests.
///
/// It must sit behind ZendeskDeletionReadOnlyHandler, so a request is
/// validated before a token is requested: a blocked request never
/// triggers sign-in and never carries a token. A 401 discards the token,
/// so it is not reused.
/// </summary>
public sealed class ZendeskDeletionBearerHandler
    : DelegatingHandler
{
    private readonly IZendeskInvalidatableTokenSource _tokenSource;

    public ZendeskDeletionBearerHandler(
        IZendeskInvalidatableTokenSource tokenSource,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenSource =
            tokenSource
            ?? throw new ArgumentNullException(nameof(tokenSource));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token =
            await _tokenSource.GetAccessTokenAsync(cancellationToken);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response =
            await base.SendAsync(
                request,
                cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _tokenSource.Invalidate();
        }

        return response;
    }
}
