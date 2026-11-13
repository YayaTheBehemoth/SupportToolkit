using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

/// <summary>
/// The web session runs the review in the background and relays the rules
/// of BackupReviewSession; it adds none of its own.
/// </summary>
public class AggregatorWebSessionTests
{
    private static AggregatorWebSession NewSession(
        out ProgressFeed feed)
    {
        var (_, _, resolver) =
            Create(
                FullConfiguration(SupportToolkitMode.Fixture),
                FullSecrets());

        feed = new ProgressFeed();

        var local = feed;

        return new AggregatorWebSession(
            new BackupReviewSession(
                BackupAggregatorComposition.Workflow(resolver, local.Write),
                BackupAggregatorComposition.ScopedWorkflow(
                    resolver,
                    progress: local.Write)),
            feed);
    }

    private static async Task WaitUntilDone(AggregatorWebSession session)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (session.IsRunning)
        {
            Assert.True(DateTime.UtcNow < deadline, "The review did not finish.");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void BeforeAnyRun_NothingIsReviewed_AndNoTicketIsReady()
    {
        var session = NewSession(out _);

        Assert.Equal(ReviewRunState.Idle, session.State);
        Assert.Null(session.Review.CurrentReview);
        Assert.False(session.PreviewTicket().IsReady);
    }

    [Fact]
    public async Task ARun_FinishesWithAReview_AndTellsThePageWhenItChanges()
    {
        var session = NewSession(out _);
        var changes = 0;

        session.Changed += () => Interlocked.Increment(ref changes);

        Assert.True(session.Start());
        await WaitUntilDone(session);

        Assert.Equal(ReviewRunState.Finished, session.State);
        Assert.NotNull(session.Review.CurrentReview);
        Assert.Null(session.Error);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task AFixtureReview_CanBePreviewed_WithoutAnyZendeskConfiguration()
    {
        var session = NewSession(out _);

        session.Start();
        await WaitUntilDone(session);

        var preview = session.PreviewTicket();

        Assert.True(preview.IsReady);
        Assert.False(string.IsNullOrWhiteSpace(preview.Value.Subject));
    }

    [Fact]
    public async Task ASecondStart_WhileRunning_IsRefused()
    {
        var gate = new TaskCompletionSource();
        var (_, _, resolver) =
            Create(
                FullConfiguration(SupportToolkitMode.Production),
                FullSecrets());

        // Held inside the production path (no external call is made).
        var session =
            new AggregatorWebSession(
                new BackupReviewSession(
                    BackupAggregatorComposition.Workflow(resolver),
                    BackupAggregatorComposition.ScopedWorkflow(
                        resolver,
                        evidenceSourceFactory:
                            async _ =>
                            {
                                await gate.Task;
                                throw new InvalidOperationException("stop");
                            })),
                new ProgressFeed());

        Assert.True(session.Start());
        Assert.True(session.IsRunning);
        Assert.False(session.Start());

        gate.SetResult();
        await WaitUntilDone(session);

        Assert.Equal(ReviewRunState.Failed, session.State);
    }

    [Fact]
    public async Task ARunThatFails_LeavesNoCurrentReview_AndSaysWhy()
    {
        var (_, _, resolver) =
            Create(
                FullConfiguration(SupportToolkitMode.Production),
                FullSecrets());

        // Production mode with an inventory factory that fails: nothing
        // external is contacted.
        var session =
            new AggregatorWebSession(
                new BackupReviewSession(
                    BackupAggregatorComposition.Workflow(resolver),
                    BackupAggregatorComposition.ScopedWorkflow(
                        resolver,
                        evidenceSourceFactory:
                            _ => throw new InvalidOperationException(
                                "Evidence is unavailable."))),
                new ProgressFeed());

        session.Start();
        await WaitUntilDone(session);

        Assert.Equal(ReviewRunState.Failed, session.State);
        Assert.Null(session.Review.CurrentReview);
        Assert.Contains("Evidence is unavailable.", session.Error);
    }

    [Fact]
    public async Task CancellingARun_LeavesNoCurrentReview()
    {
        var (_, _, resolver) =
            Create(
                FullConfiguration(SupportToolkitMode.Production),
                FullSecrets());

        var session =
            new AggregatorWebSession(
                new BackupReviewSession(
                    BackupAggregatorComposition.Workflow(resolver),
                    BackupAggregatorComposition.ScopedWorkflow(
                        resolver,
                        evidenceSourceFactory:
                            async token =>
                            {
                                await Task.Delay(Timeout.Infinite, token);
                                throw new InvalidOperationException();
                            })),
                new ProgressFeed());

        session.Start();
        await Task.Delay(50);
        session.Cancel();
        await WaitUntilDone(session);

        Assert.Equal(ReviewRunState.Cancelled, session.State);
        Assert.Null(session.Review.CurrentReview);
    }

    [Fact]
    public void TheProgressFeed_KeepsOnlyTheLatestMessages_AndIsThreadSafe()
    {
        var feed = new ProgressFeed();

        Parallel.For(0, 1000, i => feed.Write($"message {i}"));

        Assert.Equal(200, feed.Snapshot().Count);

        feed.Clear();

        Assert.Empty(feed.Snapshot());
    }
}
