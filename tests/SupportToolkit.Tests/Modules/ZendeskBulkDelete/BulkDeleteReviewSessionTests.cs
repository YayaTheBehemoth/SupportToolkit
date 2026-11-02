using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

/// <summary>
/// The rules behind the bulk delete screens: what may happen in which
/// order, and that nothing is deleted without the exact confirmation.
/// </summary>
public class BulkDeleteReviewSessionTests : IDisposable
{
    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    private static readonly ZendeskSearchCriteria Criteria =
        new(Organization: "Example Org");

    private int _searches;

    private BulkDeleteReviewSession NewSession(
        IEnumerable<ZendeskTicketSummary> tickets,
        FakeDeleteApi? api = null,
        bool complete = true,
        bool withExecutor = true)
    {
        var list = tickets.ToArray();

        return new BulkDeleteReviewSession(
            "example",
            _ => Task.FromResult(Operator),
            (_, _, _) =>
            {
                _searches++;
                return Task.FromResult(Preview(list, complete));
            },
            withExecutor ? Executor(api ?? new FakeDeleteApi(), _receipts) : null,
            new FixedClock());
    }

    private static async Task<BulkDeleteReviewSession> Searched(
        BulkDeleteReviewSession session)
    {
        await session.SignInAsync();
        Assert.Null(await session.SearchAsync(Criteria));

        return session;
    }

    // ---- Order of events ----

    [Fact]
    public async Task TheStages_FollowSignInSearchPlan()
    {
        var session = NewSession(Closed(3));

        Assert.Equal(BulkDeleteStage.SignedOut, session.Stage);

        await session.SignInAsync();
        Assert.Equal(BulkDeleteStage.SignedIn, session.Stage);
        Assert.Equal(Operator, session.Operator);

        Assert.Null(await session.SearchAsync(Criteria));
        Assert.Equal(BulkDeleteStage.Previewed, session.Stage);
        Assert.Equal(3, session.SelectedCount);

        Assert.NotNull(session.PreparePlan().Plan);
        Assert.Equal(BulkDeleteStage.Planned, session.Stage);
    }

    [Fact]
    public async Task ASearchBeforeSignIn_IsRefused()
    {
        var session = NewSession(Closed(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SearchAsync(Criteria));

        Assert.Equal(0, _searches);
    }

    [Fact]
    public async Task AnEmptySearch_IsRefusedWithAMessage_AndNothingIsSearched()
    {
        var session = NewSession(Closed(1));

        await session.SignInAsync();

        var message = await session.SearchAsync(new ZendeskSearchCriteria());

        Assert.NotNull(message);
        Assert.Equal(0, _searches);
        Assert.Equal(BulkDeleteStage.SignedIn, session.Stage);
    }

    [Fact]
    public async Task TheSearchTermsUsed_AreRemembered_AndForgottenWithTheSearch()
    {
        var session = await Searched(NewSession(Closed(2)));

        Assert.Equal(Criteria, session.Criteria);

        session.StartOver();

        Assert.Null(session.Criteria);
    }

    [Fact]
    public async Task ANewSearch_ReplacesTheSelectionAndThePlan()
    {
        var session = await Searched(NewSession(Closed(3)));

        session.PreparePlan();

        Assert.Null(await session.SearchAsync(Criteria));

        Assert.Null(session.Plan);
        Assert.Equal(BulkDeleteStage.Previewed, session.Stage);
    }

    [Fact]
    public async Task AFailedSearch_LeavesNoPreview()
    {
        var session =
            new BulkDeleteReviewSession(
                "example",
                _ => Task.FromResult(Operator),
                (_, _, _) => throw new HttpRequestException("down"),
                null);

        await session.SignInAsync();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => session.SearchAsync(Criteria));

        Assert.Null(session.Preview);
        Assert.Equal(BulkDeleteStage.SignedIn, session.Stage);
    }

    // ---- Selection and plan ----

    [Fact]
    public async Task ChangingTheSelection_DiscardsThePlan()
    {
        var session = await Searched(NewSession(Closed(3)));

        session.PreparePlan();

        Assert.Null(session.Deselect([1]));

        Assert.Null(session.Plan);
        Assert.Equal(BulkDeleteStage.Previewed, session.Stage);
        Assert.Equal(2, session.SelectedCount);
    }

    [Fact]
    public async Task ATicketClosedLessThan24HoursAgo_CannotBeSelected_AndNothingChanges()
    {
        var session =
            await Searched(
                NewSession(
                    [Ticket(1), Ticket(2, "closed", TimeSpan.FromHours(2))]));

        Assert.Equal(1, session.SelectedCount);

        var message = session.Select([2]);

        Assert.NotNull(message);
        Assert.Equal(1, session.SelectedCount);
        Assert.False(session.IsSelected(2));
    }

    [Fact]
    public async Task ATicketThatIsNotClosed_IsNotSelectedByDefault_ButCanBeAdded()
    {
        var session =
            await Searched(NewSession([Ticket(1), Ticket(2, "open")]));

        Assert.False(session.IsSelected(2));
        Assert.Null(session.Select([2]));
        Assert.True(session.IsSelected(2));
    }

    [Fact]
    public async Task SelectAll_ChoosesEveryDeletableTicket_AndLeavesOutTheOnesTooRecent()
    {
        var session =
            await Searched(
                NewSession(
                    [
                        Ticket(1),
                        Ticket(2, "open"),
                        Ticket(3, "pending"),
                        Ticket(4, "closed", TimeSpan.FromHours(2))
                    ]));

        Assert.Equal(1, session.SelectedCount);

        Assert.Null(session.SelectAll());

        Assert.Equal(3, session.SelectedCount);
        Assert.True(session.IsSelected(2));
        Assert.False(session.IsSelected(4));

        // Not-closed tickets chosen this way still need the second number.
        session.PreparePlan();

        Assert.True(session.Requirements().NeedsNonClosedConfirmation);
        Assert.Equal(2, session.Requirements().NonClosed.Count);
    }

    [Fact]
    public async Task AnIncompleteSearch_NeverBecomesAPlan()
    {
        var session =
            await Searched(NewSession(Closed(3), complete: false));

        var attempt = session.PreparePlan();

        Assert.Null(attempt.Plan);
        Assert.Contains("incomplete", attempt.Problem);
        Assert.Equal(BulkDeleteStage.Previewed, session.Stage);
    }

    [Fact]
    public async Task NothingSelected_IsNotAPlan()
    {
        var session = await Searched(NewSession(Closed(3)));

        session.ClearSelection();

        var attempt = session.PreparePlan();

        Assert.Null(attempt.Plan);
        Assert.NotNull(attempt.Problem);
    }

    [Fact]
    public async Task TheDryRun_ListsTheRequests_AndSendsNothing()
    {
        var api = new FakeDeleteApi();
        var session = await Searched(NewSession(Closed(250), api));

        session.PreparePlan();

        var batches = session.DryRun();

        Assert.Equal([100, 100, 50], batches.Select(b => b.TicketCount));
        Assert.Equal(1, batches[0].FirstId);
        Assert.Equal(250, batches[2].LastId);
        Assert.Empty(api.Calls);
    }

    // ---- Confirmation ----

    [Fact]
    public async Task AllClosed_NeedsOnlyTheCount()
    {
        var session = await Searched(NewSession(Closed(3)));

        session.PreparePlan();

        var requirements = session.Requirements();

        Assert.Equal(3, requirements.TicketCount);
        Assert.False(requirements.NeedsNonClosedConfirmation);

        Assert.False(session.CheckConfirmation("2", null).IsReady);
        Assert.False(session.CheckConfirmation("three", null).IsReady);
        Assert.False(session.CheckConfirmation("", null).IsReady);
        Assert.True(session.CheckConfirmation(" 3 ", null).IsReady);
    }

    [Fact]
    public async Task NotClosedTickets_NeedASecondConfirmationOfTheirOwnCount()
    {
        var session =
            await Searched(
                NewSession([Ticket(1), Ticket(2), Ticket(3, "open")]));

        session.Select([3]);
        session.PreparePlan();

        var requirements = session.Requirements();

        Assert.True(requirements.NeedsNonClosedConfirmation);
        Assert.Equal([3L], requirements.NonClosed.Select(t => t.Id));

        var onlyFirst = session.CheckConfirmation("3", null);
        Assert.True(onlyFirst.CountMatches);
        Assert.False(onlyFirst.NonClosedMatches);
        Assert.False(onlyFirst.IsReady);

        Assert.False(session.CheckConfirmation("3", "2").IsReady);

        var ready = session.CheckConfirmation("3", "1");

        Assert.True(ready.IsReady);
        Assert.Equal(new DeletionConfirmation(3, 1), ready.Confirmation);
    }

    // ---- Deletion ----

    [Fact]
    public async Task ADeletion_DeletesExactlyThePlan_AndReportsAClearResult()
    {
        var api = new FakeDeleteApi();
        var session = await Searched(NewSession(Closed(3), api));

        session.PreparePlan();

        await session.ExecuteAsync(
            session.CheckConfirmation("3", null).Confirmation!);

        Assert.Equal(BulkDeleteStage.Finished, session.Stage);
        Assert.Equal([1L, 2L, 3L], api.DeletedBatches.Single());

        var result = session.Result!;

        Assert.True(result.IsClean);
        Assert.Equal(3, result.Deleted);
        Assert.True(File.Exists(result.ReceiptPath));
        Assert.NotNull(session.LatestProgress);
    }

    [Fact]
    public async Task APlan_CanOnlyBeRunOnce()
    {
        var api = new FakeDeleteApi();
        var session = await Searched(NewSession(Closed(2), api));

        session.PreparePlan();

        var confirmation = session.CheckConfirmation("2", null).Confirmation!;

        await session.ExecuteAsync(confirmation);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ExecuteAsync(confirmation));

        Assert.Single(api.DeletedBatches);
    }

    [Fact]
    public async Task AWrongConfirmation_IsRefusedBeforeAnythingStarts_AndThePlanStaysUsable()
    {
        var api = new FakeDeleteApi();
        var session = await Searched(NewSession(Closed(3), api));

        session.PreparePlan();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ExecuteAsync(new DeletionConfirmation(2, null)));

        Assert.Empty(api.Calls);
        Assert.Equal(BulkDeleteStage.Planned, session.Stage);
        Assert.Empty(Directory.GetFiles(_receipts));

        await session.ExecuteAsync(new DeletionConfirmation(3, null));

        Assert.True(session.Result!.IsClean);
    }

    [Fact]
    public async Task NonClosedTicketsWithoutTheSecondNumber_AreNotDeleted()
    {
        var api = new FakeDeleteApi();
        var session =
            await Searched(NewSession([Ticket(1), Ticket(2, "open")], api));

        session.Select([2]);
        session.PreparePlan();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ExecuteAsync(new DeletionConfirmation(2, null)));

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task WithoutAnExecutor_NothingCanBeDeleted()
    {
        var session =
            await Searched(NewSession(Closed(2), withExecutor: false));

        session.PreparePlan();

        Assert.False(session.CanDelete);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ExecuteAsync(new DeletionConfirmation(2, null)));
    }

    [Fact]
    public async Task TicketsZendeskFailed_AreListedAndTheResultIsNotClean()
    {
        var api = new FakeDeleteApi { FailTicket = id => id == 2 };
        var session = await Searched(NewSession(Closed(3), api));

        session.PreparePlan();

        await session.ExecuteAsync(new DeletionConfirmation(3, null));

        var result = session.Result!;

        Assert.False(result.IsClean);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(1, result.Failed);

        var problem = Assert.Single(result.Problems);

        Assert.Equal(TicketDeletionState.Failed, problem.State);
        Assert.Equal([2L], problem.TicketIds);
    }

    [Fact]
    public async Task ACancelledDeletion_EndsFinished_WithoutAResult_AndPointsAtTheReceipt()
    {
        using var cancellation = new CancellationTokenSource();

        var api =
            new FakeDeleteApi
            {
                PollsUntilDone = 5,
                OnRequest =
                    request =>
                    {
                        if (request.Method == HttpMethod.Get)
                        {
                            cancellation.Cancel();
                        }
                    }
            };

        var session = await Searched(NewSession(Closed(3), api));

        session.PreparePlan();

        await session.ExecuteAsync(
            new DeletionConfirmation(3, null),
            cancellation.Token);

        Assert.Equal(BulkDeleteStage.Finished, session.Stage);
        Assert.Null(session.Result);
        Assert.Contains("cancelled", session.CancelledMessage);
        Assert.Single(Directory.GetFiles(_receipts));
    }

    [Fact]
    public async Task WhileDeleting_NothingCanBeChanged()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var api =
            new FakeDeleteApi
            {
                PollsUntilDone = 2,
                OnRequest =
                    request =>
                    {
                        started.TrySetResult();
                        release.Task.GetAwaiter().GetResult();
                    }
            };

        var session = await Searched(NewSession(Closed(3), api));

        session.PreparePlan();

        var running =
            Task.Run(
                () => session.ExecuteAsync(new DeletionConfirmation(3, null)));

        await started.Task;

        Assert.Equal(BulkDeleteStage.Deleting, session.Stage);
        Assert.Throws<InvalidOperationException>(() => session.Deselect([1]));
        Assert.Throws<InvalidOperationException>(() => session.StartOver());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.SearchAsync(Criteria));

        release.SetResult();
        await running;

        Assert.Equal(BulkDeleteStage.Finished, session.Stage);
    }

    [Fact]
    public async Task GoingBackFromAPlan_KeepsTheSelection_ButNotThePlan()
    {
        var session = await Searched(NewSession(Closed(3)));

        session.Deselect([1]);
        session.PreparePlan();

        session.DiscardPlan();

        Assert.Null(session.Plan);
        Assert.Equal(BulkDeleteStage.Previewed, session.Stage);
        Assert.Equal(2, session.SelectedCount);
    }

    [Fact]
    public async Task StartOver_KeepsTheSignInAndForgetsTheRest()
    {
        var session = await Searched(NewSession(Closed(2)));

        session.PreparePlan();
        await session.ExecuteAsync(new DeletionConfirmation(2, null));

        session.StartOver();

        Assert.Equal(BulkDeleteStage.SignedIn, session.Stage);
        Assert.Equal(Operator, session.Operator);
        Assert.Null(session.Preview);
        Assert.Null(session.Plan);
        Assert.Null(session.Result);
    }
}
