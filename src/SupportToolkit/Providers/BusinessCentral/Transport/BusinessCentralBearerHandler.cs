using System.Net.Http.Headers;
using SupportToolkit.Providers.BusinessCentral.Auth;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// Attaches the delegated bearer token to outgoing requests.
///
/// This handler must sit behind BusinessCentralReadOnlyHandler so a
/// request is validated before a token is requested. A blocked
/// request therefore never triggers sign-in and never carries a token.
/// </summary>
public sealed class BusinessCentralBearerHandler
    : DelegatingHandler
{
    private readonly IBusinessCentralTokenProvider _tokenProvider;

    public BusinessCentralBearerHandler(
        IBusinessCentralTokenProvider tokenProvider,
        HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        _tokenProvider =
            tokenProvider
            ?? throw new ArgumentNullException(
                nameof(tokenProvider)
            );
    }

    protected override async Task<HttpResponseMessage>
        SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        var token =
            await _tokenProvider.GetAccessTokenAsync(
                cancellationToken
            );

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token
            );

        return await base.SendAsync(
            request,
            cancellationToken
        );
    }
}
