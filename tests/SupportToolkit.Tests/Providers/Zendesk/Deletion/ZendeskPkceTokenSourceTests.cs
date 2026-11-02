using System.Net;
using System.Text.Json;
using System.Web;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

public class ZendeskPkceTokenSourceTests
{
    private static ZendeskDeletionOptions Options() =>
        ZendeskDeletionOptions.Create("example", "example-public-client");

    [Fact]
    public void Challenge_MatchesTheRfc7636Vector()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            ZendeskPkce.CreateChallenge(
                "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void VerifierAndState_AreRandomBase64Url()
    {
        var first = ZendeskPkce.CreateVerifier();
        var second = ZendeskPkce.CreateVerifier();

        Assert.NotEqual(first, second);
        Assert.Equal(43, first.Length);
        Assert.All(
            first,
            character => Assert.True(
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'));

        Assert.NotEqual(
            ZendeskPkce.CreateState(),
            ZendeskPkce.CreateState());
    }

    [Fact]
    public async Task SignIn_SendsAPkceRequest_AndExchangesTheCodeWithoutASecret()
    {
        var harness = new Harness();

        var token = await harness.Source.GetAccessTokenAsync();

        Assert.Equal("example-access-token", token);

        // Authorization request.
        var authorize = harness.AuthorizeUrl!;

        Assert.Equal("https", authorize.Scheme);
        Assert.Equal("example.zendesk.com", authorize.Host);
        Assert.Equal("/oauth/authorizations/new", authorize.AbsolutePath);

        var query = HttpUtility.ParseQueryString(authorize.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("example-public-client", query["client_id"]);
        Assert.Equal(harness.Receiver.RedirectUri, query["redirect_uri"]);
        Assert.Equal("read tickets:write", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));

        // Token request: same redirect URI, verifier matches the
        // challenge, and no client secret of any kind is sent.
        var body = JsonDocument.Parse(harness.TokenRequestBody!).RootElement;

        Assert.Equal(
            "authorization_code",
            body.GetProperty("grant_type").GetString());

        Assert.Equal(
            "example-code",
            body.GetProperty("code").GetString());

        Assert.Equal(
            harness.Receiver.RedirectUri,
            body.GetProperty("redirect_uri").GetString());

        Assert.Equal(
            "read tickets:write",
            body.GetProperty("scope").GetString());

        Assert.Equal(
            query["code_challenge"],
            ZendeskPkce.CreateChallenge(
                body.GetProperty("code_verifier").GetString()!));

        Assert.DoesNotContain(
            "secret",
            harness.TokenRequestBody!,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal("/oauth/tokens", harness.TokenRequestPath);
        Assert.Null(harness.TokenRequestAuthorization);
    }

    [Fact]
    public async Task Token_IsReused_WithoutSigningInAgain()
    {
        var harness = new Harness();

        await harness.Source.GetAccessTokenAsync();
        await harness.Source.GetAccessTokenAsync();

        Assert.Equal(1, harness.BrowserOpens);
        Assert.Equal(1, harness.TokenRequests);
    }

    [Fact]
    public async Task StateMismatch_IsRejected_AndTheCodeIsNeverExchanged()
    {
        var harness =
            new Harness(
                redirect: (state, _) =>
                    new Dictionary<string, string>
                    {
                        ["code"] = "example-code",
                        ["state"] = "not-the-state"
                    });

        var exception =
            await Assert.ThrowsAsync<ZendeskAuthenticationException>(
                () => harness.Source.GetAccessTokenAsync());

        Assert.Contains("state", exception.Message);
        Assert.Equal(0, harness.TokenRequests);
    }

    [Fact]
    public async Task MissingState_IsRejected()
    {
        var harness =
            new Harness(
                redirect: (_, _) =>
                    new Dictionary<string, string>
                    {
                        ["code"] = "example-code"
                    });

        await Assert.ThrowsAsync<ZendeskAuthenticationException>(
            () => harness.Source.GetAccessTokenAsync());

        Assert.Equal(0, harness.TokenRequests);
    }

    [Fact]
    public async Task ErrorRedirect_IsReported_WithoutExchangingAnything()
    {
        var harness =
            new Harness(
                redirect: (state, _) =>
                    new Dictionary<string, string>
                    {
                        ["error"] = "access_denied",
                        ["error_description"] = "The user said no.",
                        ["state"] = state
                    });

        var exception =
            await Assert.ThrowsAsync<ZendeskAuthenticationException>(
                () => harness.Source.GetAccessTokenAsync());

        Assert.Contains("access_denied", exception.Message);
        Assert.Contains("The user said no.", exception.Message);
        Assert.Equal(0, harness.TokenRequests);
    }

    [Fact]
    public async Task RedirectWithoutACode_IsRejected()
    {
        var harness =
            new Harness(
                redirect: (state, _) =>
                    new Dictionary<string, string> { ["state"] = state });

        await Assert.ThrowsAsync<ZendeskAuthenticationException>(
            () => harness.Source.GetAccessTokenAsync());
    }

    [Fact]
    public async Task NoRedirect_TimesOutCleanly()
    {
        var receiver = new FakeReceiver();

        var source =
            new ZendeskPkceTokenSource(
                new HttpClient(new StubHandler(_ => Ok())),
                Options(),
                new FakeBrowser(_ => { }),
                () => receiver,
                signInTimeout: TimeSpan.FromMilliseconds(50));

        var exception =
            await Assert.ThrowsAsync<ZendeskAuthenticationException>(
                () => source.GetAccessTokenAsync());

        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_AsCancellation()
    {
        var receiver = new FakeReceiver();
        using var cancel = new CancellationTokenSource();

        var source =
            new ZendeskPkceTokenSource(
                new HttpClient(new StubHandler(_ => Ok())),
                Options(),
                new FakeBrowser(_ => cancel.Cancel()),
                () => receiver);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetAccessTokenAsync(cancel.Token));
    }

    [Fact]
    public async Task TokenEndpointFailure_ReportsOnlyTheStatus_NeverTheBody()
    {
        var harness =
            new Harness(
                tokenResponse: _ =>
                    new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent(
                            "echoed-secret-value-from-body")
                    });

        var exception =
            await Assert.ThrowsAsync<ZendeskAuthenticationException>(
                () => harness.Source.GetAccessTokenAsync());

        Assert.Contains("400", exception.Message);
        Assert.DoesNotContain(
            "echoed-secret-value-from-body",
            exception.Message);
    }

    [Theory]
    [InlineData("""{"access_token":"","token_type":"bearer","expires_in":60}""")]
    [InlineData("""{"access_token":"x","token_type":"mac","expires_in":60}""")]
    [InlineData("""{"access_token":"x","token_type":"bearer","expires_in":0}""")]
    [InlineData("""{"token_type":"bearer"}""")]
    public async Task InvalidTokenResponse_IsRejected(string json)
    {
        var harness =
            new Harness(tokenResponse: _ => Json(json));

        await Assert.ThrowsAsync<ZendeskAuthenticationException>(
            () => harness.Source.GetAccessTokenAsync());
    }

    [Fact]
    public async Task MissingLifetime_IsNotAssumed_TokenIsUsedUntilInvalidated()
    {
        var clock = new AdjustableClock();

        var harness =
            new Harness(
                clock: clock,
                tokenResponse: _ => Json(
                    """{"access_token":"example-access-token","token_type":"bearer"}"""));

        Assert.Equal(
            "example-access-token",
            await harness.Source.GetAccessTokenAsync());

        // Days later the token is still used: no lifetime was invented.
        clock.Advance(TimeSpan.FromDays(3));

        Assert.Equal(
            "example-access-token",
            await harness.Source.GetAccessTokenAsync());

        Assert.Equal(1, harness.BrowserOpens);

        // A rejected token is dropped and not silently replaced.
        harness.Source.Invalidate();

        await Assert.ThrowsAsync<ZendeskAuthenticationException>(
            () => harness.Source.GetAccessTokenAsync());

        Assert.Equal(1, harness.BrowserOpens);

        // An explicit sign-in works again.
        await harness.Source.SignInAsync();

        Assert.Equal(2, harness.BrowserOpens);
    }

    [Fact]
    public async Task ExpiredToken_RequiresAnExplicitNewSignIn()
    {
        var clock = new AdjustableClock();

        var harness =
            new Harness(
                clock: clock,
                tokenResponse: _ => Json(
                    """{"access_token":"example-access-token","token_type":"bearer","expires_in":300}"""));

        await harness.Source.GetAccessTokenAsync();

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.False(harness.Source.IsSignedIn);

        var exception =
            await Assert.ThrowsAsync<ZendeskAuthenticationException>(
                () => harness.Source.GetAccessTokenAsync());

        Assert.Contains("Sign in again", exception.Message);
        Assert.Equal(1, harness.BrowserOpens);
    }

    [Fact]
    public async Task Logs_NeverContainTheTokenCodeOrVerifier()
    {
        var logs = new StringWriter();

        var harness =
            new Harness(
                logger: new OperationalLogger(
                    debugEnabled: true,
                    output: logs));

        await harness.Source.GetAccessTokenAsync();

        var text = logs.ToString();

        Assert.NotEmpty(text);
        Assert.DoesNotContain("example-access-token", text);
        Assert.DoesNotContain("example-code", text);
        Assert.DoesNotContain(
            JsonDocument.Parse(harness.TokenRequestBody!).RootElement
                .GetProperty("code_verifier").GetString()!,
            text);
    }

    private static HttpResponseMessage Ok() =>
        Json(
            """{"access_token":"example-access-token","token_type":"bearer","expires_in":3600}""");

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                System.Text.Encoding.UTF8,
                "application/json")
        };

    /// <summary>
    /// A fake browser and redirect receiver: opening the authorization
    /// URL immediately "redirects" back with whatever the test supplies.
    /// </summary>
    private sealed class Harness
    {
        private readonly Func<string, string, Dictionary<string, string>>
            _redirect;

        public Harness(
            Func<string, string, Dictionary<string, string>>? redirect = null,
            Func<HttpRequestMessage, HttpResponseMessage>? tokenResponse = null,
            TimeProvider? clock = null,
            OperationalLogger? logger = null)
        {
            _redirect =
                redirect
                ?? ((state, _) => new Dictionary<string, string>
                {
                    ["code"] = "example-code",
                    ["state"] = state
                });

            Receiver = new FakeReceiver();

            var browser =
                new FakeBrowser(
                    url =>
                    {
                        BrowserOpens++;
                        AuthorizeUrl = url;

                        var query = HttpUtility.ParseQueryString(url.Query);

                        Receiver.Complete(
                            _redirect(
                                query["state"]!,
                                query["code_challenge"]!));
                    });

            var http =
                new HttpClient(
                    new StubHandler(
                        request =>
                        {
                            TokenRequests++;
                            TokenRequestPath = request.RequestUri!.AbsolutePath;
                            TokenRequestAuthorization =
                                request.Headers.Authorization;
                            TokenRequestBody =
                                request.Content!
                                    .ReadAsStringAsync().GetAwaiter()
                                    .GetResult();

                            return (tokenResponse ?? (_ => Ok()))(request);
                        }));

            Source =
                new ZendeskPkceTokenSource(
                    http,
                    Options(),
                    browser,
                    () =>
                    {
                        Receiver = new FakeReceiver();
                        return Receiver;
                    },
                    clock,
                    logger);

            // Make the first receiver the one created by the factory.
            Receiver = null!;
        }

        public FakeReceiver Receiver { get; private set; }

        public ZendeskPkceTokenSource Source { get; }

        public Uri? AuthorizeUrl { get; private set; }

        public int BrowserOpens { get; private set; }

        public int TokenRequests { get; private set; }

        public string? TokenRequestBody { get; private set; }

        public string? TokenRequestPath { get; private set; }

        public object? TokenRequestAuthorization { get; private set; }
    }

    private sealed class FakeBrowser : IZendeskBrowserLauncher
    {
        private readonly Action<Uri> _onOpen;

        public FakeBrowser(Action<Uri> onOpen)
        {
            _onOpen = onOpen;
        }

        public void Open(Uri url) => _onOpen(url);
    }

    private sealed class FakeReceiver : IZendeskLoopbackReceiver
    {
        private readonly TaskCompletionSource<
            IReadOnlyDictionary<string, string>> _redirect = new();

        public string RedirectUri => "http://localhost:51234";

        public void Complete(IReadOnlyDictionary<string, string> values) =>
            _redirect.TrySetResult(values);

        public Task<IReadOnlyDictionary<string, string>>
            WaitForRedirectAsync(
                CancellationToken cancellationToken) =>
            _redirect.Task.WaitAsync(cancellationToken);

        public void Dispose()
        {
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>
            _respond;

        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private sealed class AdjustableClock : TimeProvider
    {
        private DateTimeOffset _now =
            new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
