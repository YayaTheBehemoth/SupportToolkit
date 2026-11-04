using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using System.Net;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

/// <summary>
/// The Zendesk bulk delete page, served by the real host. No Zendesk is
/// contacted: the connection delegates are never called by these tests.
/// </summary>
public sealed class ZendeskHostFixture : IAsyncLifetime
{
    public RunningWebHost Available { get; private set; } = default!;
    public RunningWebHost Unavailable { get; private set; } = default!;

    public HttpClient Client { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var (service, _, _) =
            Create(
                FullConfiguration(SupportToolkitMode.Fixture),
                FullSecrets());

        static BulkDeleteWebSession Web(BulkDeleteAvailability availability) =>
            new(
                _ => Task.FromResult(availability),
                _ => throw new InvalidOperationException(
                    "Not expected: nothing signs in during these tests."));

        Available =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(
                    service,
                    null,
                    Web(new BulkDeleteAvailability(true, "example", null))));

        Unavailable =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(
                    service,
                    null,
                    Web(
                        new BulkDeleteAvailability(
                            false,
                            null,
                            "No Zendesk deletion connection is selected."))));

        Client =
            new HttpClient(
                new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Available.DisposeAsync();
        await Unavailable.DisposeAsync();
    }
}

public class ZendeskPageTests : IClassFixture<ZendeskHostFixture>
{
    private readonly ZendeskHostFixture _fixture;

    public ZendeskPageTests(ZendeskHostFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpResponseMessage> Get(
        RunningWebHost host,
        string path,
        bool signedIn)
    {
        string? cookie = null;

        if (signedIn)
        {
            using var launch = await _fixture.Client.GetAsync(host.LaunchUrl);

            cookie =
                launch.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        }

        var request =
            new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + path);

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await _fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task ThePage_NeedsTheSessionCookie()
    {
        using var response = await Get(_fixture.Available, "/zendesk", false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhenAvailable_ItWarnsItDeletesRealTickets_AndOffersOnlySignIn()
    {
        using var response = await Get(_fixture.Available, "/zendesk", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("This deletes real tickets from example.zendesk.com", html);
        Assert.Contains("Sign in to Zendesk", html);

        // Nothing to search, choose or delete before signing in.
        Assert.DoesNotContain("Search</button>", html);
        Assert.DoesNotContain("permanently", html);
    }

    [Fact]
    public async Task TheStepGuide_ShowsWhereYouAre_AndTheSwitchStartsOnSimple()
    {
        using var response = await Get(_fixture.Available, "/zendesk", true);

        var html = await response.Content.ReadAsStringAsync();

        foreach (var step in new[] { "Sign in", "Search", "Choose tickets", "Confirm" })
        {
            Assert.Contains(step, html);
        }

        Assert.Contains("class=\"current\"", html);
        Assert.Contains("Simple", html);
        Assert.Contains("Detailed", html);
    }

    [Fact]
    public async Task WhenUnavailable_ItSaysWhy_AndOffersNothing()
    {
        using var response = await Get(_fixture.Unavailable, "/zendesk", true);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("not available for this profile", html);
        Assert.Contains("No Zendesk deletion connection is selected.", html);
        Assert.DoesNotContain("Sign in to Zendesk", html);
    }

    [Fact]
    public async Task TheNavigation_LinksToThePage()
    {
        using var response = await Get(_fixture.Available, "/", true);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"zendesk\"", html);
    }
}
