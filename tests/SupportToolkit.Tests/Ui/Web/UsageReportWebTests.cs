using System.Net;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Fixtures;
using SupportToolkit.Modules.UsageReport.Services;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

internal static class UsageReportWebSupport
{
    public static SupportToolkitConfigurationResolver Resolver(
        SupportToolkitMode mode,
        IReadOnlyList<string>? items = null)
    {
        var configuration = FullConfiguration(SupportToolkitMode.Fixture);
        var profile = configuration.Profiles[configuration.ActiveProfile!];

        profile.Modules["usage-report"] = mode;
        profile.MonitoringScope["usage-report"] =
            new ModuleMonitoringScopeConfiguration
            {
                ServiceItemNumbers = (items ?? [FixtureUsageReportSources.BackupItem]).ToList()
            };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        return resolver;
    }

    public static UsageReportWebSession Web(
        SupportToolkitConfigurationResolver resolver,
        Func<CancellationToken, Task<IUsageReportSources>>? productionSources = null)
    {
        var progress = new ProgressFeed();

        return new UsageReportWebSession(
            new UsageReportSession(
                new UsageReportWorkflow(resolver, progress.Write, productionSources)),
            progress);
    }
}

/// <summary>
/// The web session runs the report in the background and adds no rules of
/// its own.
/// </summary>
public class UsageReportWebSessionTests
{
    private static async Task WaitUntilDone(UsageReportWebSession web)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (web.IsRunning)
        {
            Assert.True(DateTime.UtcNow < deadline, "The report did not finish.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task ARun_ProducesTheReport_AndTellsThePage()
    {
        var web = UsageReportWebSupport.Web(UsageReportWebSupport.Resolver(SupportToolkitMode.Fixture));
        var changes = 0;

        web.Changed += () => Interlocked.Increment(ref changes);

        Assert.True(web.Start());
        await WaitUntilDone(web);

        Assert.Equal(ReviewRunState.Finished, web.State);
        Assert.NotNull(web.Report.CurrentReport);
        Assert.NotEmpty(web.Progress.Snapshot());
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task ASecondStart_WhileRunning_IsRefused()
    {
        var gate = new TaskCompletionSource();

        var web =
            UsageReportWebSupport.Web(
                UsageReportWebSupport.Resolver(SupportToolkitMode.Production),
                async _ =>
                {
                    await gate.Task;
                    return new FixtureUsageReportSources();
                });

        Assert.True(web.Start());
        Assert.True(web.IsRunning);
        Assert.False(web.Start());

        gate.SetResult();
        await WaitUntilDone(web);

        Assert.Equal(ReviewRunState.Finished, web.State);
    }

    [Fact]
    public async Task AFailedRun_SaysWhy_AndShowsNoReport()
    {
        var web =
            UsageReportWebSupport.Web(
                UsageReportWebSupport.Resolver(SupportToolkitMode.Production),
                _ => throw new InvalidOperationException("Business Central is unavailable."));

        web.Start();
        await WaitUntilDone(web);

        Assert.Equal(ReviewRunState.Failed, web.State);
        Assert.Contains("Business Central is unavailable.", web.Error);
        Assert.Null(web.Report.CurrentReport);
    }

    [Fact]
    public async Task ACancelledRun_ShowsNoReport()
    {
        var web =
            UsageReportWebSupport.Web(
                UsageReportWebSupport.Resolver(SupportToolkitMode.Production),
                async token =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return new FixtureUsageReportSources();
                });

        web.Start();
        await Task.Delay(50);
        web.Cancel();
        await WaitUntilDone(web);

        Assert.Equal(ReviewRunState.Cancelled, web.State);
        Assert.Null(web.Report.CurrentReport);
    }
}

public sealed class UsageReportHostFixture : IAsyncLifetime
{
    public RunningWebHost Available { get; private set; } = default!;
    public RunningWebHost Unavailable { get; private set; } = default!;
    public RunningWebHost Generated { get; private set; } = default!;

    public HttpClient Client { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var (service, _, _) =
            Create(FullConfiguration(SupportToolkitMode.Fixture), FullSecrets());

        Available =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(
                    service,
                    usageReport: UsageReportWebSupport.Web(
                        UsageReportWebSupport.Resolver(SupportToolkitMode.Fixture))));

        // Production without any backup items configured.
        Unavailable =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(
                    service,
                    usageReport: UsageReportWebSupport.Web(
                        UsageReportWebSupport.Resolver(SupportToolkitMode.Production, items: []))));

        // A report already generated, so the page renders it on first load.
        var generated =
            UsageReportWebSupport.Web(
                UsageReportWebSupport.Resolver(SupportToolkitMode.Fixture));

        generated.Start();

        while (generated.IsRunning)
        {
            await Task.Delay(20);
        }

        Generated =
            await WebHostRunner.StartAsync(
                new WebServeOptions(0, false),
                new WebAppState(service, usageReport: generated));

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
        await Generated.DisposeAsync();
    }
}

/// <summary>
/// The usage report page, served by the real host. Nothing external is
/// contacted: fixture mode, and an unavailable production profile.
/// </summary>
public class UsageReportPageTests : IClassFixture<UsageReportHostFixture>
{
    private readonly UsageReportHostFixture _fixture;

    public UsageReportPageTests(UsageReportHostFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<HttpResponseMessage> Get(
        RunningWebHost host,
        string path,
        bool signedIn)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, host.BaseUrl + path);

        if (signedIn)
        {
            using var launch = await _fixture.Client.GetAsync(host.LaunchUrl);

            request.Headers.Add(
                "Cookie",
                launch.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        }

        return await _fixture.Client.SendAsync(request);
    }

    [Fact]
    public async Task ThePage_NeedsTheSessionCookie()
    {
        using var response = await Get(_fixture.Available, "/usage", false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThePage_ShowsTheMode_AndOffersToGenerate()
    {
        using var response = await Get(_fixture.Available, "/usage", true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Usage report", html);
        Assert.Contains("FIXTURE: synthetic data only", html);
        Assert.Contains("Generate report", html);

        // Nothing is shown before a report has been generated.
        Assert.DoesNotContain("compared", html);
    }

    [Fact]
    public async Task AnUnavailableProfile_SaysWhy_AndOffersNothing()
    {
        using var response = await Get(_fixture.Unavailable, "/usage", true);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("The usage report is not available.", html);
        Assert.Contains("No backup items", html);
        Assert.DoesNotContain("Generate report", html);
    }

    [Fact]
    public async Task AGeneratedReport_IsShown_WithTheListsApart()
    {
        using var response = await Get(_fixture.Generated, "/usage", true);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("Fjeldsted Example Revision", html);
        Assert.Contains("-20.00", html);
        Assert.Contains("compared", html);
        Assert.Contains("Set up, no usage reported yet", html);
        Assert.Contains("Bakkegaard Example Tandlaeger", html);
        Assert.Contains("Storage without a backup subscription", html);
        // Simple view: the notes and special cases are in the Detailed view.
        Assert.DoesNotContain("not meant for billing", html);
        Assert.Contains("special case(s)", html);
        Assert.DoesNotContain("Tranum Example Holding", html);

        // Simple view: not-active customers are counted, not listed.
        Assert.Contains("The Detailed view lists them", html);
        Assert.DoesNotContain("Saltholm Example Marine", html);
    }

    [Fact]
    public async Task TheNavigation_LinksToThePage()
    {
        using var response = await Get(_fixture.Available, "/", true);

        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("href=\"usage\"", html);
    }
}
