using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using System.Net;
using System.Text.Json;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

public class ZendeskDeletionExecutorTests : IDisposable
{
    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    private static DeletionConfirmation Confirm(
        DeletionPlan plan) =>
        new(
            plan.Tickets.Count,
            plan.NonClosed.Count > 0 ? plan.NonClosed.Count : null);

    [Fact]
    public async Task ABatch_IsDeleted_AndEveryTicketIsReportedDeleted()
    {
        var api = new FakeDeleteApi { PollsUntilDone = 2 };
        var plan = Plan(Closed(3));
        var events = new List<DeletionProgress>();

        var run =
            await Executor(api, _receipts)
                .ExecuteAsync(plan, Confirm(plan), events.Add);

        Assert.True(run.Outcome.IsComplete);
        Assert.Equal(3, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.Equal([1L, 2L, 3L], api.DeletedBatches.Single());

        // The job was followed to its end.
        Assert.Equal("DELETE", api.Calls[0].Method);
        Assert.Equal(2, api.Calls.Count(call => call.Method == "GET"));

        Assert.Contains(
            events,
            e => e.Phase == DeletionProgressPhase.WaitingForJob);

        Assert.Contains(
            events,
            e => e.Phase == DeletionProgressPhase.BatchFinished);
    }

    [Fact]
    public async Task OnlyTheDeleteAndJobStatusRequests_AreEverSent()
    {
        var api = new FakeDeleteApi();
        var plan = Plan(Closed(250));

        await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.All(
            api.Calls,
            call =>
                Assert.True(
                    (call.Method == "DELETE"
                     && call.PathAndQuery.StartsWith(
                         "/api/v2/tickets/destroy_many.json?ids=",
                         StringComparison.Ordinal))
                    || (call.Method == "GET"
                        && call.PathAndQuery.StartsWith(
                            "/api/v2/job_statuses/",
                            StringComparison.Ordinal))));
    }

    [Fact]
    public async Task ManyTickets_GoInBatchesOfAHundred_OneJobAtATime()
    {
        var api = new FakeDeleteApi { PollsUntilDone = 2 };
        var plan = Plan(Closed(250));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.True(run.Outcome.IsComplete);

        Assert.Equal(
            [100, 100, 50],
            api.DeletedBatches.Select(batch => batch.Length));

        // The next request starts only after the previous job finished.
        var sequence = api.Calls.Select(call => call.Method).ToArray();

        Assert.Equal(
            ["DELETE", "GET", "GET", "DELETE", "GET", "GET", "DELETE", "GET", "GET"],
            sequence);
    }

    [Fact]
    public async Task OnlyTheConfirmedIds_AreSent_AndTheSearchIsNeverRunAgain()
    {
        var api = new FakeDeleteApi();
        var plan = Plan(Closed(5), include: []);

        await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal([1L, 2L, 3L, 4L, 5L], api.DeletedBatches.Single());
        Assert.DoesNotContain(
            api.Calls,
            call => call.PathAndQuery.Contains("search"));
    }

    [Fact]
    public async Task ATicketZendeskDidNotDelete_IsFailed_AndTheRunContinues()
    {
        var api = new FakeDeleteApi { FailTicket = id => id == 2 };
        var plan = Plan(Closed(250));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        var failed = Assert.Single(run.Outcome.Results, r => r.State == TicketDeletionState.Failed);

        Assert.Equal(2, failed.Id);
        Assert.Equal(249, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.False(run.Outcome.IsComplete);

        // The later batches were still sent.
        Assert.Equal(3, api.DeletedBatches.Count);
    }

    [Fact]
    public async Task AFailedJob_IsUnknown_NotDeleted_AndTheRunStops()
    {
        var api =
            new FakeDeleteApi
            {
                FinalStatus = "failed",
                OmitResult = _ => true
            };

        var plan = Plan(Closed(250));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(0, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.Equal(100, run.Outcome.Count(TicketDeletionState.Unknown));
        Assert.Equal(150, run.Outcome.Count(TicketDeletionState.NotAttempted));
        Assert.Contains("Job failed", run.Outcome.Results[0].Detail);

        // Only the first batch was ever sent.
        Assert.Single(api.DeletedBatches);
    }

    [Fact]
    public async Task AFailedJob_OnlyTrustsTicketsItExplicitlyReportedDeleted()
    {
        // The job failed but did report tickets 1-50 as deleted.
        var api =
            new FakeDeleteApi
            {
                FinalStatus = "failed",
                OmitResult = id => id > 50
            };

        var plan = Plan(Closed(150));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.Unknown));
        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.NotAttempted));
        Assert.False(run.Outcome.IsComplete);
    }

    [Fact]
    public async Task AJobThatNeverFinishes_IsUnknown_AndTheRunStops()
    {
        var api = new FakeDeleteApi { PollsUntilDone = int.MaxValue };
        var plan = Plan(Closed(150));

        var run =
            await Executor(api, _receipts, TimeSpan.FromSeconds(10))
                .ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(100, run.Outcome.Count(TicketDeletionState.Unknown));
        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.NotAttempted));
        Assert.Contains(
            run.Outcome.Results,
            r => r.Detail!.Contains("still running"));

        Assert.Single(api.DeletedBatches);
    }

    [Fact]
    public async Task ATicketWithNoResult_IsUnknown_NeverAssumedDeleted()
    {
        var api = new FakeDeleteApi { OmitResult = id => id == 3 };
        var plan = Plan(Closed(5));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        var unknown = Assert.Single(run.Outcome.Results, r => r.State == TicketDeletionState.Unknown);

        Assert.Equal(3, unknown.Id);
        Assert.Equal(4, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.False(run.Outcome.IsComplete);
    }

    [Fact]
    public async Task ARejectedRequest_IsFailedNotDeleted_AndStopsTheRun()
    {
        var api =
            new FakeDeleteApi
            {
                DeleteOverride = _ =>
                    FakeDeleteApi.Json(HttpStatusCode.Forbidden, "customer-body")
            };

        var plan = Plan(Closed(150));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(100, run.Outcome.Count(TicketDeletionState.Failed));
        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.NotAttempted));
        Assert.Contains("403", run.Outcome.Results[0].Detail);
        Assert.DoesNotContain("customer-body", run.Outcome.Results[0].Detail);
        Assert.Single(api.DeletedBatches);
    }

    [Fact]
    public async Task AServerError_OnTheRequest_IsUnknown_BecauseTheJobMayHaveStarted()
    {
        var api =
            new FakeDeleteApi
            {
                DeleteOverride = _ =>
                    FakeDeleteApi.Json(HttpStatusCode.InternalServerError, "{}")
            };

        var plan = Plan(Closed(3));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(3, run.Outcome.Count(TicketDeletionState.Unknown));
        Assert.Equal(0, run.Outcome.Count(TicketDeletionState.Deleted));
        Assert.Contains("deleted tickets", run.Outcome.Results[0].Detail);
    }

    [Fact]
    public async Task ANetworkFailure_OnTheRequest_IsUnknown()
    {
        var api =
            new FakeDeleteApi
            {
                OnRequest = _ => throw new HttpRequestException("connection lost")
            };

        var plan = Plan(Closed(3));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(3, run.Outcome.Count(TicketDeletionState.Unknown));

        // The receipt names the underlying failure, not a wrapper.
        Assert.Contains(
            "did not complete clearly (HttpRequestException)",
            run.Outcome.Results[0].Detail);
    }

    [Fact]
    public async Task AFailedPoll_IsUnknown_BecauseTheJobWasStarted()
    {
        var api =
            new FakeDeleteApi
            {
                JobOverride = _ =>
                    FakeDeleteApi.Json(HttpStatusCode.NotFound, "{}")
            };

        var plan = Plan(Closed(150));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(100, run.Outcome.Count(TicketDeletionState.Unknown));
        Assert.Equal(50, run.Outcome.Count(TicketDeletionState.NotAttempted));
        Assert.Equal("J1", run.Outcome.Results[0].JobId);

        Assert.Contains(
            "could not be read (ZendeskApiException)",
            run.Outcome.Results[0].Detail);
    }

    [Fact]
    public async Task AnExpiredSignIn_IsRecordedAsNothingSent_WithAFinalReceipt()
    {
        var api =
            new FakeDeleteApi
            {
                OnRequest = _ => throw new ZendeskAuthenticationException(
                    "The Zendesk sign-in has expired. Sign in again.")
            };

        var plan = Plan(Closed(3));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal(3, run.Outcome.Count(TicketDeletionState.Failed));
        Assert.Contains("Not signed in", run.Outcome.Results[0].Detail);
        Assert.Equal("finished", ReadReceipt(run.ReceiptPath).GetProperty("state").GetString());
    }

    [Fact]
    public async Task AWrongCount_IsRefused_AndNothingIsSentOrWritten()
    {
        var api = new FakeDeleteApi();
        var plan = Plan(Closed(3));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Executor(api, _receipts)
                .ExecuteAsync(plan, new DeletionConfirmation(2, null)));

        Assert.Empty(api.Calls);
        Assert.Empty(Directory.GetFiles(_receipts));
    }

    [Fact]
    public async Task NonClosedTickets_NeedTheSecondConfirmation()
    {
        var api = new FakeDeleteApi();
        var plan = Plan([Ticket(1), Ticket(2, "open")], include: [2]);

        Assert.Single(plan.NonClosed);

        // Missing, or wrong, second confirmation.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Executor(api, _receipts)
                .ExecuteAsync(plan, new DeletionConfirmation(2, null)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Executor(api, _receipts)
                .ExecuteAsync(plan, new DeletionConfirmation(2, 2)));

        Assert.Empty(api.Calls);

        var run =
            await Executor(api, _receipts)
                .ExecuteAsync(plan, new DeletionConfirmation(2, 1));

        Assert.True(run.Outcome.IsComplete);
    }

    [Fact]
    public async Task APlanWithNoNonClosedTickets_RefusesAStrayNonClosedConfirmation()
    {
        var api = new FakeDeleteApi();
        var plan = Plan(Closed(2));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Executor(api, _receipts)
                .ExecuteAsync(plan, new DeletionConfirmation(2, 5)));

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task TheReceipt_IsWrittenBeforeTheFirstDeleteRequest()
    {
        string? stateAtFirstDelete = null;
        string[]? idsAtFirstDelete = null;

        var api = new FakeDeleteApi();

        api.OnRequest =
            request =>
            {
                if (request.Method != HttpMethod.Delete
                    || stateAtFirstDelete is not null)
                {
                    return;
                }

                var file = Assert.Single(Directory.GetFiles(_receipts));
                var receipt = ReadReceipt(file);

                stateAtFirstDelete = receipt.GetProperty("state").GetString();
                idsAtFirstDelete =
                    receipt.GetProperty("tickets").EnumerateArray()
                        .Select(t => t.GetProperty("id").GetInt64().ToString())
                        .ToArray();
            };

        var plan = Plan(Closed(3));

        await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        Assert.Equal("started", stateAtFirstDelete);
        Assert.NotNull(idsAtFirstDelete);
        Assert.Equal(["1", "2", "3"], idsAtFirstDelete!);
    }

    [Fact]
    public async Task IfTheReceiptCannotBeWritten_NothingIsDeleted()
    {
        // A file where the receipt folder should be.
        var blocker = Path.Combine(_receipts, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "x");

        var api = new FakeDeleteApi();
        var plan = Plan(Closed(3));

        await Assert.ThrowsAnyAsync<IOException>(
            () => Executor(api, blocker).ExecuteAsync(plan, Confirm(plan)));

        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task TheFinalReceipt_ListsEveryTicketAndItsOutcome_WithoutContent()
    {
        var api = new FakeDeleteApi { FailTicket = id => id == 2 };
        var plan = Plan(Closed(3));

        var run =
            await Executor(api, _receipts).ExecuteAsync(plan, Confirm(plan));

        var text = await File.ReadAllTextAsync(run.ReceiptPath);
        var receipt = JsonDocument.Parse(text).RootElement;

        Assert.Equal("finished", receipt.GetProperty("state").GetString());
        Assert.Equal("example", receipt.GetProperty("instance").GetString());
        Assert.Equal(
            "type:ticket example",
            receipt.GetProperty("query").GetString());
        Assert.Equal(7, receipt.GetProperty("operator").GetProperty("id").GetInt64());
        Assert.Equal(3, receipt.GetProperty("summary").GetProperty("planned").GetInt32());
        Assert.Equal(2, receipt.GetProperty("summary").GetProperty("deleted").GetInt32());
        Assert.Equal(1, receipt.GetProperty("summary").GetProperty("failed").GetInt32());

        var outcomes =
            receipt.GetProperty("tickets").EnumerateArray()
                .ToDictionary(
                    t => t.GetProperty("id").GetInt64(),
                    t => t.GetProperty("outcome").GetString());

        Assert.Equal("deleted", outcomes[1], ignoreCase: true);
        Assert.Equal("failed", outcomes[2], ignoreCase: true);

        // No ticket subject or content ever reaches the receipt.
        Assert.DoesNotContain("Example subject", text);
        Assert.Equal(run.ReceiptPath, Directory.GetFiles(_receipts).Single());
    }

    [Fact]
    public async Task Cancelling_MidRun_StillWritesAFinalReceipt_AndMarksTheBatchUnknown()
    {
        using var cancel = new CancellationTokenSource();

        var api = new FakeDeleteApi { PollsUntilDone = 5 };

        var executor =
            new ZendeskDeletionExecutor(
                new ZendeskTicketDeleter(Provider(api)),
                new DeletionReceiptStore(_receipts),
                new FixedClock(),
                (_, token) =>
                {
                    cancel.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });

        var plan = Plan(Closed(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                plan,
                Confirm(plan),
                null,
                cancel.Token));

        var file = Assert.Single(Directory.GetFiles(_receipts));
        var receipt = ReadReceipt(file);

        Assert.Equal("finished", receipt.GetProperty("state").GetString());
        Assert.Equal(100, receipt.GetProperty("summary").GetProperty("unknown").GetInt32());
        Assert.Equal(50, receipt.GetProperty("summary").GetProperty("notAttempted").GetInt32());
    }

    [Fact]
    public async Task APlan_CanBeRunOnlyWithItsOwnConfirmation_NotAnother()
    {
        var api = new FakeDeleteApi();
        var small = Plan(Closed(2));
        var large = Plan(Closed(4));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Executor(api, _receipts)
                .ExecuteAsync(large, Confirm(small)));

        Assert.Empty(api.Calls);
    }

    private static JsonElement ReadReceipt(string path) =>
        JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
}
