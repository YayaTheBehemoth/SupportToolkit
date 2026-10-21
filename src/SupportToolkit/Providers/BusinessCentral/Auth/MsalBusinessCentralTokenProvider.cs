using Microsoft.Identity.Client;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Providers.BusinessCentral.Auth;

public enum BusinessCentralSignInMode
{
    InteractiveBrowser,
    DeviceCode
}

/// <summary>
/// Delegated sign-in using a public client and PKCE.
///
/// Tokens live only in MSAL's in-memory cache for the lifetime of the
/// process. No token cache serialization is registered, so nothing is
/// written to disk or Credential Manager, and no client secret exists.
///
/// The operator signs in once per run. If the session can no longer be
/// refreshed silently, the request fails instead of prompting again.
///
/// MSAL logging is not enabled, so MSAL never emits token or account
/// details.
/// </summary>
public sealed class MsalBusinessCentralTokenProvider
    : IBusinessCentralTokenProvider
{
    private static readonly TimeSpan SignInTimeout =
        TimeSpan.FromMinutes(5);

    private static readonly string[] Scopes =
        [BusinessCentralOptions.DelegatedScope];

    private readonly BusinessCentralOptions _options;
    private readonly BusinessCentralSignInMode _signInMode;
    private readonly TextWriter _prompt;
    private readonly OperationalLogger? _logger;
    private readonly IPublicClientApplication _application;

    private readonly SemaphoreSlim _gate =
        new(1, 1);

    private IAccount? _account;

    public MsalBusinessCentralTokenProvider(
        BusinessCentralOptions options,
        BusinessCentralSignInMode signInMode =
            BusinessCentralSignInMode.InteractiveBrowser,
        TextWriter? prompt = null,
        OperationalLogger? logger = null)
    {
        _options =
            options
            ?? throw new ArgumentNullException(nameof(options));

        _signInMode = signInMode;

        _prompt =
            prompt ?? Console.Out;

        _logger = logger;

        _application =
            PublicClientApplicationBuilder
                .Create(options.ClientId)
                .WithAuthority(options.AuthorityUri)
                .WithRedirectUri("http://localhost")
                .Build();
    }

    /// <summary>
    /// Account shown to the operator so a wrong sign-in is noticed.
    /// Never logged.
    /// </summary>
    public string? SignedInAccount =>
        _account?.Username;

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_account is not null)
            {
                return await AcquireSilentAsync(
                    _account,
                    cancellationToken
                );
            }

            var result =
                await AcquireInteractiveAsync(
                    cancellationToken
                );

            if (!string.Equals(
                    result.TenantId,
                    _options.TenantId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessCentralAuthenticationException(
                    "Signed in to an unexpected Microsoft Entra " +
                    "tenant. Sign in with an account from the " +
                    "configured tenant."
                );
            }

            _account =
                result.Account;

            _logger?.Info(
                "Business Central sign-in succeeded."
            );

            return result.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> AcquireSilentAsync(
        IAccount account,
        CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await _application
                    .AcquireTokenSilent(
                        Scopes,
                        account
                    )
                    .ExecuteAsync(cancellationToken);

            return result.AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            throw new BusinessCentralAuthenticationException(
                "The Business Central sign-in expired during the " +
                "run. Run the command again to sign in.",
                exception
            );
        }
        catch (MsalException exception)
        {
            throw new BusinessCentralAuthenticationException(
                "Business Central sign-in could not be refreshed.",
                exception
            );
        }
    }

    private async Task<AuthenticationResult>
        AcquireInteractiveAsync(
            CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    cancellationToken
                );

        timeout.CancelAfter(SignInTimeout);

        try
        {
            if (_signInMode
                == BusinessCentralSignInMode.DeviceCode)
            {
                return await _application
                    .AcquireTokenWithDeviceCode(
                        Scopes,
                        callback =>
                            _prompt.WriteLineAsync(
                                callback.Message
                            )
                    )
                    .ExecuteAsync(timeout.Token);
            }

            _logger?.Info(
                "Opening the system browser for sign-in."
            );

            await _prompt.WriteLineAsync(
                "Sign in to Business Central in the browser " +
                "window that opens."
            );

            return await _application
                .AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(
                    new SystemWebViewOptions
                    {
                        HtmlMessageSuccess =
                            "Signed in. You can close this tab " +
                            "and return to SupportToolkit.",

                        HtmlMessageError =
                            "Sign-in did not complete. Return " +
                            "to SupportToolkit."
                    }
                )
                .ExecuteAsync(timeout.Token);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new BusinessCentralAuthenticationException(
                "Business Central sign-in timed out."
            );
        }
        catch (MsalException exception)
        {
            throw new BusinessCentralAuthenticationException(
                "Business Central sign-in failed or was cancelled.",
                exception
            );
        }
    }
}
