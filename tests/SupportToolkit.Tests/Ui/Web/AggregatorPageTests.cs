using SupportToolkit.Composition;
using System.Net;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

public sealed class AggregatorHostFixture : IAsyncLifetime
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
        var (service, _, resolver) =
            Create(
                FullConfiguration(SupportToolkitMode.Fixture),
                FullSecrets());

        var progress = new ProgressFeed();

        Host =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(
                    service,
                    new AggregatorWebSession(
                        new BackupReviewSession(
                            BackupAggregatorComposition.Workflow(resolver),
                            BackupAggregatorComposition.ScopedWorkflow(resolver)),
                        progress)));

        Client = new HttpClient(_handler);
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
    }
}

/// <summary>
/// The Backup Aggregator page, served by the real host in fixture mode.
/// Interactive behaviour is covered by the session tests; this checks the
/// page is reachable, gated and says what mode it is in.
/// </summary>
public class AggregatorPageTests : IClassFixture<AggregatorHostFixture>
{
    private readonly AggregatorHostFixture _fixture;

    public AggregatorPageTests(AggregatorHostFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<string> SignIn()
    {
        using var response =
            await _fixture.Client.GetAsync(_fixture.Host.LaunchUrl);

        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private async Task<HttpResponseMessage> Get(string path, string? cookie)
    {
        var request =
            new HttpRequestMessage(HttpMethod.Get, _fixture.Host.BaseUrl + path);

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", cookie);
        }

        return await _fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task ThePage_NeedsTheSessionCookie()
    {
        using var response = await Get("/aggregator", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThePage_ShowsItIsFixtureData_AndOffersTheReview()
    {
        using var response = await Get("/aggregator", await SignIn());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Backup Aggregator", html);
        Assert.Contains("FIXTURE: synthetic data only", html);
        Assert.DoesNotContain("PRODUCTION: real customer data", html);
        Assert.Contains("Run review", html);
    }

    [Fact]
    public async Task NothingIsShownAsReviewed_BeforeAReviewHasRun()
    {
        using var response = await Get("/aggregator", await SignIn());

        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Complete review", html);
        Assert.DoesNotContain("Incomplete review", html);
        Assert.DoesNotContain("Create ticket", html);
    }

    [Fact]
    public async Task TheNavigation_LinksToThePage()
    {
        using var response = await Get("/", await SignIn());

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"aggregator\"", html);
        Assert.Contains("Exit SupportToolkit", html);
        Assert.Contains("leaves it running in the background", html);
    }
}
