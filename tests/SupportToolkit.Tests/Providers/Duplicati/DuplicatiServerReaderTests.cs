using System.Net;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Duplicati.Transport;
using static SupportToolkit.Tests.Providers.Duplicati.FakeDuplicatiServer;

namespace SupportToolkit.Tests.Providers.Duplicati;

/// <summary>
/// A job's latest outcome, decided from Duplicati's log and metadata as
/// the lab recorded them. Times are synthetic.
/// </summary>
public class DuplicatiServerReaderTests
{
    private static readonly ResolvedDuplicatiServer Server =
        new("machine-a", new Uri("https://backup.example.com/"), Password);

    private static async Task<DuplicatiJob> ReadSingleAsync(FakeDuplicatiServer server)
    {
        var jobs =
            await new DuplicatiServerReader(DuplicatiHttpPipeline.Create(server))
                .ReadJobsAsync(Server, CancellationToken.None);

        return Assert.Single(jobs);
    }

    private static Dictionary<string, string> Metadata(
        string? finished = null,
        string? error = null,
        string? version = null)
    {
        var metadata = new Dictionary<string, string>();

        if (finished is not null) metadata["LastBackupFinished"] = finished;
        if (error is not null)
        {
            metadata["LastErrorDate"] = error;
            metadata["LastErrorMessage"] = "The folder /home/someone/secret-path does not exist";
        }
        if (version is not null) metadata["LastBackupDate"] = version;

        return metadata;
    }

    [Fact]
    public async Task ASuccessfulRun_IsSuccess_WithItsTimes()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents",
                    Metadata(finished: "20261120T115204Z", version: "20261120T115203Z"),
                    new Run("Success", "2026-11-20T11:52:04.0443302Z")));

        Assert.Equal(DuplicatiJobOutcome.Success, job.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T11:52:04.0443302Z"), job.LastRun);
        Assert.Equal(job.LastRun, job.LastSuccess);
        Assert.False(job.IsRunning);
    }

    [Fact]
    public async Task AWarningShowsOnlyInTheLog_AndIsStillAWarning()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Partly missing",
                    Metadata(finished: "20261120T115556Z", version: "20261120T115556Z"),
                    new Run("Warning", "2026-11-20T11:55:56.4Z")));

        Assert.Equal(DuplicatiJobOutcome.Warning, job.Outcome);
        Assert.NotNull(job.LastSuccess);
    }

    [Fact]
    public async Task AFailureBeforeTheJobCouldLog_IsFailed_FromTheMetadata()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "2", "Broken destination", Metadata(error: "20261120T115254Z")));

        Assert.Equal(DuplicatiJobOutcome.Failed, job.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T11:52:54Z"), job.LastRun);
        Assert.Null(job.LastSuccess);
    }

    [Fact]
    public async Task AnOldError_DoesNotOutliveALaterSuccess()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "5", "Recovers",
                    Metadata(finished: "20261120T115430Z", error: "20261120T115429Z", version: "20261120T115430Z"),
                    new Run("Success", "2026-11-20T11:54:30.52Z")));

        Assert.Equal(DuplicatiJobOutcome.Success, job.Outcome);
    }

    [Fact]
    public async Task AnErrorAfterTheLastLoggedRun_IsFailed()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "5", "Recovers",
                    Metadata(finished: "20261120T115430Z", error: "20261120T115523Z"),
                    new Run("Success", "2026-11-20T11:54:30.52Z")));

        Assert.Equal(DuplicatiJobOutcome.Failed, job.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T11:55:23Z"), job.LastRun);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T11:54:30.52Z"), job.LastSuccess);
    }

    [Fact]
    public async Task ALoggedFatalRun_InTheSameSecondAsTheError_IsFailed()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "5", "Recovers",
                    Metadata(finished: "20261120T115430Z", error: "20261120T115523Z"),
                    new Run("Fatal", "2026-11-20T11:55:23.5956931Z"),
                    new Run("Success", "2026-11-20T11:54:30.52Z")));

        Assert.Equal(DuplicatiJobOutcome.Failed, job.Outcome);
        Assert.Equal(DateTimeOffset.Parse("2026-11-20T11:54:30.52Z"), job.LastSuccess);
    }

    [Fact]
    public async Task ASuccess_InTheSameSecondAsAnError_IsUnknown_NotAGuess()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents",
                    Metadata(finished: "20261120T115204Z", error: "20261120T115204Z"),
                    new Run("Success", "2026-11-20T11:52:04.9Z")));

        Assert.Equal(DuplicatiJobOutcome.Unknown, job.Outcome);
        Assert.Contains("same second", job.Detail);
    }

    [Fact]
    public async Task AJobThatNeverRan_IsNeverRun()
    {
        var job = await ReadSingleAsync(new FakeDuplicatiServer().AddJob("2", "Never run"));

        Assert.Equal(DuplicatiJobOutcome.NeverRun, job.Outcome);
        Assert.Null(job.LastRun);
    }

    [Theory]
    [InlineData("20261120 11:52", null)]
    [InlineData(null, "2026-11-20T11:52:04Z")]
    public async Task AnUnreadableTimestamp_IsUnknown(string? finished, string? error)
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents",
                    Metadata(finished: finished, error: error),
                    new Run("Success", "2026-11-20T11:52:04Z")));

        Assert.Equal(DuplicatiJobOutcome.Unknown, job.Outcome);
    }

    [Fact]
    public async Task AFinishedRunTheLogDoesNotShow_IsUnknown()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents", Metadata(finished: "20261120T120000Z"),
                    new Run("Success", "2026-11-20T11:52:04Z")));

        Assert.Equal(DuplicatiJobOutcome.Unknown, job.Outcome);
    }

    [Theory]
    [InlineData("Error", false, DuplicatiJobOutcome.Failed)]
    [InlineData("Success", true, DuplicatiJobOutcome.Failed)]
    [InlineData("Paused", false, DuplicatiJobOutcome.Unknown)]
    public async Task OtherResults_AreFailedOrUnknown_NeverSuccess(
        string result,
        bool interrupted,
        DuplicatiJobOutcome expected)
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents", Metadata(finished: "20261120T115204Z"),
                    new Run(result, "2026-11-20T11:52:04Z", interrupted)));

        Assert.Equal(expected, job.Outcome);
    }

    [Fact]
    public async Task OnlyBackupRunsCount_NotACompactOrRestoreLoggedLater()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "1", "Documents", Metadata(finished: "20261120T115204Z"),
                    new Run("Success", "2026-11-20T12:30:00Z", Operation: "Restore"),
                    new Run("Error", "2026-11-20T11:52:04Z")));

        Assert.Equal(DuplicatiJobOutcome.Failed, job.Outcome);
    }

    [Fact]
    public async Task TheRunningJob_IsMarkedRunning()
    {
        var server =
            new FakeDuplicatiServer { RunningJobId = "2" }
                .AddJob("1", "Documents")
                .AddJob("2", "Pictures");

        var jobs =
            await new DuplicatiServerReader(DuplicatiHttpPipeline.Create(server))
                .ReadJobsAsync(Server, CancellationToken.None);

        Assert.False(jobs.Single(job => job.Id == "1").IsRunning);
        Assert.True(jobs.Single(job => job.Id == "2").IsRunning);
    }

    // ---------------------------------------------------------------
    // Transport
    // ---------------------------------------------------------------

    [Fact]
    public async Task TheReader_SignsIn_ThenOnlyReads_WithTheToken()
    {
        var server = new FakeDuplicatiServer().AddJob("1", "Documents");

        await ReadSingleAsync(server);

        Assert.Equal(HttpMethod.Post, server.Requests[0].Method);
        Assert.Equal("/api/v1/auth/login", server.Requests[0].Path);
        Assert.All(server.Requests.Skip(1), request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"Bearer {Token}", request.Authorization);
        });
        Assert.Contains(server.Requests, request => request.Path == "/api/v1/backup/1/log?pagesize=20");
    }

    [Fact]
    public async Task NeitherTheDestinationNorErrorTextIsKept()
    {
        var job =
            await ReadSingleAsync(
                new FakeDuplicatiServer().AddJob(
                    "2", "Broken destination", Metadata(error: "20261120T115254Z"),
                    new Run("Error", "2026-11-20T11:52:54Z")));

        var everything = job.ToString();

        Assert.DoesNotContain(TargetSecret, everything);
        Assert.DoesNotContain("backup.example.com/2", everything);
        Assert.DoesNotContain("secret-path", everything);
    }

    [Fact]
    public async Task AWrongPassword_IsRefused_WithoutRepeatingIt()
    {
        var reader = new DuplicatiServerReader(DuplicatiHttpPipeline.Create(new FakeDuplicatiServer()));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => reader.ReadJobsAsync(
                    Server with { Password = "EXAMPLE-WRONG-PASSWORD" },
                    CancellationToken.None));

        Assert.Contains("refused the password", error.Message);
        Assert.DoesNotContain("EXAMPLE-WRONG-PASSWORD", error.Message);
    }

    [Fact]
    public async Task AServerError_FailsTheWholeServer()
    {
        var server =
            new FakeDuplicatiServer { FailEverythingWith = HttpStatusCode.ServiceUnavailable }
                .AddJob("1", "Documents");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new DuplicatiServerReader(DuplicatiHttpPipeline.Create(server))
                .ReadJobsAsync(Server, CancellationToken.None));
    }

    [Theory]
    [InlineData("POST", "https://backup.example.com/api/v1/backup/1/start")]
    [InlineData("DELETE", "https://backup.example.com/api/v1/backup/1")]
    [InlineData("PUT", "https://backup.example.com/api/v1/backup/1")]
    [InlineData("GET", "https://backup.example.com/index.html")]
    public async Task ThePipeline_RefusesAnyWrite_BeforeItIsSent(string method, string url)
    {
        var server = new FakeDuplicatiServer();
        using var client = DuplicatiHttpPipeline.Create(server);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)));

        Assert.Empty(server.Requests);
    }

    [Theory]
    [InlineData("https://backup.example.com", "https://backup.example.com/")]
    [InlineData("http://127.0.0.1:8201", "http://127.0.0.1:8201/")]
    public void AnHttpsUrl_OrLoopbackHttp_IsAccepted(string url, string expected)
    {
        Assert.Equal(expected, DuplicatiOptions.ParseServerUrl(url, "machine-a").ToString());
    }

    [Theory]
    [InlineData("http://backup.example.com/")]
    [InlineData("https://user:pass@backup.example.com/")]
    [InlineData("https://backup.example.com/?x=1")]
    [InlineData("")]
    public void AnythingElse_IsRefused(string url)
    {
        Assert.Throws<InvalidOperationException>(
            () => DuplicatiOptions.ParseServerUrl(url, "machine-a"));
    }
}
