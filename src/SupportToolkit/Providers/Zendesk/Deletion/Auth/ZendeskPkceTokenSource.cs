using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Deletion.Auth;

/// <summary>
/// A token source whose token can be discarded after Zendesk rejects it.
/// </summary>
public interface IZendeskInvalidatableTokenSource
    : IZendeskAccessTokenSource
{
    void Invalidate();
}

/// <summary>
/// Signs an operator in to the Zendesk deletion connection: OAuth
/// authorization code with PKCE (S256) in the system browser, for a
/// public client.
///
/// No client secret exists and none is sent. The token lives in memory
/// only and is gone when the process exits. It is never logged.
///
/// The first token request signs in. After that the token is reused until
/// it expires or Zendesk rejects it; it is then not silently replaced,
/// and the caller must sign in again explicitly (SignInAsync). A missing
/// token lifetime is treated as unknown, not as a default.
/// </summary>
public sealed class ZendeskPkceTokenSource
    : IZendeskInvalidatableTokenSource
{
    private static readonly TimeSpan DefaultSignInTimeout =
        TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly ZendeskDeletionOptions _options;
    private readonly IZendeskBrowserLauncher _browser;
    private readonly Func<IZendeskLoopbackReceiver> _receiverFactory;
    private readonly TimeProvider _timeProvider;
    private readonly OperationalLogger? _logger;
    private readonly TimeSpan _signInTimeout;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset? _expiresAt;
    private bool _hasSignedIn;

    public ZendeskPkceTokenSource(
        HttpClient httpClient,
        ZendeskDeletionOptions options,
        IZendeskBrowserLauncher browser,
        Func<IZendeskLoopbackReceiver> receiverFactory,
        TimeProvider? timeProvider = null,
        OperationalLogger? logger = null,
        TimeSpan? signInTimeout = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _options =
            options ?? throw new ArgumentNullException(nameof(options));

        _browser =
            browser ?? throw new ArgumentNullException(nameof(browser));

        _receiverFactory =
            receiverFactory
            ?? throw new ArgumentNullException(nameof(receiverFactory));

        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _signInTimeout = signInTimeout ?? DefaultSignInTimeout;
    }

    public bool IsSignedIn
    {
        get
        {
            var now = _timeProvider.GetUtcNow();

            return IsUsable(now);
        }
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now = _timeProvider.GetUtcNow();

            if (IsUsable(now))
            {
                return _accessToken!;
            }

            if (_hasSignedIn)
            {
                throw new ZendeskAuthenticationException(
                    "The Zendesk sign-in has expired or was rejected. " +
                    "Sign in again.");
            }

            await SignInCoreAsync(cancellationToken);

            return _accessToken!;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens the browser and signs the operator in, replacing any
    /// previous token.
    /// </summary>
    public async Task SignInAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await SignInCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = null;
    }

    private bool IsUsable(DateTimeOffset now) =>
        _accessToken is not null
        && (_expiresAt is null
            || now < _expiresAt.Value.AddMinutes(-1));

    private async Task SignInCoreAsync(
        CancellationToken cancellationToken)
    {
        Invalidate();

        var verifier = ZendeskPkce.CreateVerifier();
        var challenge = ZendeskPkce.CreateChallenge(verifier);
        var state = ZendeskPkce.CreateState();

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        timeout.CancelAfter(_signInTimeout);

        using var receiver = _receiverFactory();

        var redirectUri = receiver.RedirectUri;

        var authorizeUrl =
            BuildAuthorizeUrl(redirectUri, state, challenge);

        _logger?.Info("Waiting for Zendesk sign-in in the browser.");

        IReadOnlyDictionary<string, string> redirect;

        try
        {
            // Listen before the browser opens so no redirect is missed.
            var waiting =
                receiver.WaitForRedirectAsync(timeout.Token);

            _browser.Open(authorizeUrl);

            redirect = await waiting;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new ZendeskAuthenticationException(
                "Zendesk sign-in timed out. Run the command again to " +
                "sign in.");
        }

        if (redirect.TryGetValue("error", out var error))
        {
            redirect.TryGetValue("error_description", out var description);

            throw new ZendeskAuthenticationException(
                "Zendesk sign-in was not completed: " +
                Clean(error) +
                (string.IsNullOrWhiteSpace(description)
                    ? string.Empty
                    : " (" + Clean(description) + ")"));
        }

        // The anti-forgery state must come back unchanged.
        if (!redirect.TryGetValue("state", out var returnedState)
            || !FixedTimeEquals(returnedState, state))
        {
            throw new ZendeskAuthenticationException(
                "Zendesk sign-in was rejected: the returned state did " +
                "not match this sign-in attempt.");
        }

        if (!redirect.TryGetValue("code", out var code)
            || string.IsNullOrWhiteSpace(code))
        {
            throw new ZendeskAuthenticationException(
                "Zendesk sign-in returned no authorization code.");
        }

        await ExchangeCodeAsync(
            code,
            redirectUri,
            verifier,
            timeout.Token);

        _hasSignedIn = true;

        _logger?.Info("Zendesk sign-in succeeded.");
    }

    private Uri BuildAuthorizeUrl(
        string redirectUri,
        string state,
        string challenge)
    {
        var query =
            "response_type=code" +
            $"&client_id={Uri.EscapeDataString(_options.ClientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(ZendeskDeletionOptions.SignInScope)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256";

        return new Uri(
            _options.BaseUri,
            "oauth/authorizations/new?" + query);
    }

    private async Task ExchangeCodeAsync(
        string code,
        string redirectUri,
        string verifier,
        CancellationToken cancellationToken)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(_options.BaseUri, "oauth/tokens"))
            {
                Content =
                    JsonContent.Create(
                        new ZendeskPkceTokenRequestDto
                        {
                            Code = code,
                            ClientId = _options.ClientId,
                            RedirectUri = redirectUri,
                            Scope = ZendeskDeletionOptions.SignInScope,
                            CodeVerifier = verifier
                        })
            };

        HttpResponseMessage response;

        try
        {
            response =
                await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new ZendeskAuthenticationException(
                "Zendesk could not be reached to complete the sign-in.",
                exception);
        }

        using (response)
        {
            _logger?.Info(
                $"POST /oauth/tokens -> {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                // The body is not read or shown: it may echo values.
                throw new ZendeskAuthenticationException(
                    "Zendesk refused the sign-in token request " +
                    $"(status {(int)response.StatusCode}).");
            }

            var token =
                await response.Content
                    .ReadFromJsonAsync<ZendeskPkceTokenResponseDto>(
                        cancellationToken: cancellationToken)
                ?? throw new ZendeskAuthenticationException(
                    "Zendesk returned an empty sign-in token response.");

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new ZendeskAuthenticationException(
                    "Zendesk returned a sign-in response without an " +
                    "access token.");
            }

            if (!string.Equals(
                    token.TokenType,
                    "bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ZendeskAuthenticationException(
                    "Zendesk returned an unexpected sign-in token type.");
            }

            if (token.ExpiresIn is <= 0)
            {
                throw new ZendeskAuthenticationException(
                    "Zendesk returned an invalid sign-in token lifetime.");
            }

            _accessToken = token.AccessToken;

            // No lifetime reported: unknown, not assumed. The token is
            // used until Zendesk rejects it.
            _expiresAt =
                token.ExpiresIn is { } seconds
                    ? _timeProvider.GetUtcNow().AddSeconds(seconds)
                    : null;
        }
    }

    private static bool FixedTimeEquals(
        string left,
        string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    // Server-supplied text is shown trimmed and without control characters.
    private static string Clean(
        string value) =>
        new string(
            value
                .Where(character => !char.IsControl(character))
                .Take(200)
                .ToArray());
}
