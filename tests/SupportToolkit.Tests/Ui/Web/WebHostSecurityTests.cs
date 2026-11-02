using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

/// <summary>
/// Starts the real web host on a loopback port in this process and talks to
/// it over HTTP. It never reaches any other machine.
/// </summary>
public sealed class WebHostFixture : IAsyncLifetime
{
    private readonly HttpClientHandler _handler =
        new()
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };

    public RunningWebHost Host { get; private set; } = default!;

    public HttpClient Client { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var (service, _, _) =
            Create(
                FullConfiguration(SupportToolkitMode.Fixture),
                FullSecrets());

        Host =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(service));

        Client = new HttpClient(_handler);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
    }
}

public class WebHostSecurityTests : IClassFixture<WebHostFixture>
{
    private readonly WebHostFixture _fixture;

    public WebHostSecurityTests(WebHostFixture fixture)
    {
        _fixture = fixture;
    }

    private RunningWebHost Host => _fixture.Host;

    private string Url(string path = "/") => Host.BaseUrl + path;

    private async Task<HttpResponseMessage> Send(
        HttpMethod method,
        string path,
        string? cookie = null,
        string? host = null,
        string? origin = null)
    {
        var request = new HttpRequestMessage(method, Url(path));

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        if (host is not null)
        {
            request.Headers.Host = host;
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await _fixture.Client.SendAsync(request);
    }

    /// <summary>Does what the browser does with the link it is given.</summary>
    private async Task<string> SignIn()
    {
        using var response =
            await _fixture.Client.GetAsync(Host.LaunchUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var setCookie =
            response.Headers.GetValues("Set-Cookie").Single();

        return setCookie.Split(';')[0];
    }

    [Fact]
    public async Task WithoutTheCookie_NothingIsServed()
    {
        using var response = await Send(HttpMethod.Get, "/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("<h1", body);
        Assert.DoesNotContain("Status", body);
    }

    [Theory]
    [InlineData("/app.css")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_blazor")]
    [InlineData("/anything")]
    public async Task EveryPath_NeedsTheCookie_NotJustThePage(string path)
    {
        using var response = await Send(HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AWrongLaunchToken_IsRefused()
    {
        using var response =
            await _fixture.Client.GetAsync(Url("/?t=not-the-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TheLaunchToken_IsOnlyHonouredAtTheRoot()
    {
        using var response =
            await _fixture.Client.GetAsync(
                Url($"/app.css?t={Host.Security.LaunchToken}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TheLaunchToken_IsNotAcceptedOnAPost()
    {
        using var response =
            await Send(
                HttpMethod.Post,
                $"/?t={Host.Security.LaunchToken}",
                origin: Host.BaseUrl);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task TheTokenExchange_SetsAStrictHttpOnlyCookie_AndRedirectsToACleanUrl()
    {
        using var response =
            await _fixture.Client.GetAsync(Host.LaunchUrl);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // The token never stays in the address bar.
        Assert.Equal("/", response.Headers.Location!.OriginalString);

        var setCookie =
            response.Headers.GetValues("Set-Cookie").Single();

        Assert.StartsWith($"{LaunchSecurity.CookieName}=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);

        // The cookie is a different secret from the launch token.
        Assert.DoesNotContain(Host.Security.LaunchToken, setCookie);
    }

    [Fact]
    public async Task WithTheCookie_TheHomePageShowsTheProfileSelector_AndNoSecret()
    {
        var cookie = await SignIn();

        using var response = await Send(HttpMethod.Get, "/", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<h1", html);
        Assert.Contains("Profile", html);
        Assert.Contains("Spare", html);
        // The connection detail is hidden for now.
        Assert.DoesNotContain("Saved connections", html);
        Assert.DoesNotContain("Environment overrides", html);
        Assert.Contains("FIXTURE: synthetic data only", html);

        Assert.Contains("Acronis", html);

        Assert.DoesNotContain(AcronisSecret, html);
        Assert.DoesNotContain(ZendeskSecret, html);
        Assert.DoesNotContain(Host.Security.LaunchToken, html);
        Assert.DoesNotContain(Host.Security.SessionValue, html);
    }

    [Fact]
    public async Task TheStylesheet_IsServedFromTheApplicationItself()
    {
        var cookie = await SignIn();

        using var response = await Send(HttpMethod.Get, "/app.css", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/css", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("--bg", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BlazorsScript_IsServed_BehindTheSameGate()
    {
        var cookie = await SignIn();

        using var response =
            await Send(HttpMethod.Get, "/_framework/blazor.web.js", cookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var script = await response.Content.ReadAsByteArrayAsync();

        Assert.True(script.Length > 10_000, "The script was empty.");
    }

    [Theory]
    [InlineData("evil.test")]
    [InlineData("evil.test:1")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("localhost.evil.test")]
    public async Task AWrongHostHeader_IsRefused_EvenWithAValidCookie(
        string host)
    {
        var cookie = await SignIn();

        using var response =
            await Send(HttpMethod.Get, "/", cookie, host);

        Assert.Equal(
            HttpStatusCode.MisdirectedRequest,
            response.StatusCode);

        Assert.DoesNotContain(
            "<h1",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheLocalhostName_Works_ForTheSamePort()
    {
        var cookie = await SignIn();

        using var response =
            await Send(
                HttpMethod.Get,
                "/",
                cookie,
                host: $"localhost:{Host.Port}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AStateChangingRequest_NeedsTheUisOwnOrigin()
    {
        var cookie = await SignIn();

        using var none =
            await Send(HttpMethod.Post, "/", cookie);

        using var foreign =
            await Send(
                HttpMethod.Post,
                "/",
                cookie,
                origin: "http://evil.test");

        using var sameHostWrongPort =
            await Send(
                HttpMethod.Post,
                "/",
                cookie,
                origin: "http://127.0.0.1:1");

        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sameHostWrongPort.StatusCode);

        // From its own origin the request passes the gate: whatever it
        // gets afterwards is the application's answer, not a refusal.
        using var own =
            await Send(
                HttpMethod.Post,
                "/",
                cookie,
                origin: Host.BaseUrl);

        Assert.NotEqual(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, own.StatusCode);
        Assert.NotEqual(HttpStatusCode.MisdirectedRequest, own.StatusCode);
    }

    [Fact]
    public async Task ALoneCookieIsNotEnoughToChangeState_WithoutTheOrigin()
    {
        var cookie = await SignIn();

        using var response =
            await Send(HttpMethod.Delete, "/", cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheLiveConnection_CanBeNegotiated_OnlyWithTheCookieAndOwnOrigin()
    {
        var cookie = await SignIn();

        const string Path = "/_blazor/negotiate?negotiateVersion=1";

        using var ok =
            await Send(
                HttpMethod.Post,
                Path,
                cookie,
                origin: Host.BaseUrl);

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains(
            "connectionId",
            await ok.Content.ReadAsStringAsync());

        using var noCookie =
            await Send(HttpMethod.Post, Path, origin: Host.BaseUrl);

        using var foreignOrigin =
            await Send(
                HttpMethod.Post,
                Path,
                cookie,
                origin: "http://evil.test");

        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreignOrigin.StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_AreOnEveryResponse_IncludingRefusals()
    {
        var cookie = await SignIn();

        var responses =
            new[]
            {
                await Send(HttpMethod.Get, "/", cookie),
                await Send(HttpMethod.Get, "/"),
                await Send(HttpMethod.Get, "/", cookie, "evil.test"),
                await Send(HttpMethod.Post, "/", cookie)
            };

        foreach (var response in responses)
        {
            using (response)
            {
                // Blazor adds its own frame-ancestors policy to a page as a
                // second header; ours must be there, and nothing may
                // allow framing.
                var policies =
                    response.Headers.GetValues("Content-Security-Policy")
                        .ToArray();

                Assert.Contains(
                    policies,
                    policy => policy.Contains("default-src 'self'")
                              && policy.Contains("frame-ancestors 'none'"));

                Assert.DoesNotContain(
                    policies,
                    policy => policy.Contains("frame-ancestors 'self'"));

                Assert.Contains(
                    response.Headers.GetValues("Cache-Control"),
                    value => value.Contains("no-store"));

                Assert.Equal(
                    "nosniff",
                    response.Headers.GetValues("X-Content-Type-Options").Single());

                Assert.Equal(
                    "DENY",
                    response.Headers.GetValues("X-Frame-Options").Single());

                Assert.Equal(
                    "no-referrer",
                    response.Headers.GetValues("Referrer-Policy").Single());

                Assert.False(response.Headers.Contains("Server"));
            }
        }
    }

    [Fact]
    public async Task NothingIsLoadedFromAnotherSite_AndNoCorsIsGranted()
    {
        var cookie = await SignIn();

        using var response =
            await Send(
                HttpMethod.Get,
                "/",
                cookie,
                origin: "http://evil.test");

        var html = await response.Content.ReadAsStringAsync();

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"));

        // Text such as a connection's origin may appear on the page, but
        // nothing is loaded from another address.
        Assert.DoesNotMatch(
            new System.Text.RegularExpressions.Regex(
                "(src|href|action)=\"(https?:)?//",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            html);

        Assert.DoesNotContain("http://evil", html);
    }

    [Fact]
    public async Task TwoLaunches_HaveSeparateSecrets()
    {
        var (service, _, _) = Create(FullConfiguration());

        await using var other =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(service));

        Assert.NotEqual(Host.Port, other.Port);
        Assert.NotEqual(
            Host.Security.LaunchToken,
            other.Security.LaunchToken);

        // The first host's token and cookie mean nothing to the second.
        using var wrongToken =
            await _fixture.Client.GetAsync(
                other.BaseUrl + $"/?t={Host.Security.LaunchToken}");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);

        var request =
            new HttpRequestMessage(HttpMethod.Get, other.BaseUrl + "/");

        request.Headers.Add(
            "Cookie",
            $"{LaunchSecurity.CookieName}={Host.Security.SessionValue}");

        using var wrongCookie = await _fixture.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongCookie.StatusCode);
    }

    [Fact]
    public async Task TheServer_ListensOnLoopbackOnly()
    {
        Assert.StartsWith("http://127.0.0.1:", Host.BaseUrl);

        // It must not accept connections on any other address of this
        // machine, so nothing else on the network can reach it.
        var others =
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address =>
                    address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address))
                .ToArray();

        foreach (var address in others)
        {
            using var client = new TcpClient();
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(2));

            var connected = true;

            try
            {
                await client.ConnectAsync(address, Host.Port, timeout.Token);
            }
            catch (Exception exception)
                when (exception is SocketException
                          or OperationCanceledException)
            {
                connected = false;
            }

            Assert.False(
                connected,
                $"The server accepted a connection on {address}.");
        }
    }
}
