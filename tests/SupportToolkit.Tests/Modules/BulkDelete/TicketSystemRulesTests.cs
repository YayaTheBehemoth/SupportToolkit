using SupportToolkit.Modules.BulkDelete.Models;
using SupportToolkit.Modules.BulkDelete.Production;
using SupportToolkit.Modules.BulkDelete.Services;
using static SupportToolkit.Tests.Modules.BulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.BulkDelete;

/// <summary>
/// Each ticketing system declares its deletion rules. Zendesk's produce
/// exactly the sentences bulk delete has always shown; a system with no
/// waiting time, no archive and no restore is handled by the same module
/// without any of Zendesk's limits.
/// </summary>
public class TicketSystemRulesTests : IDisposable
{
    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    /// <summary>
    /// A fictional system that deletes one ticket per request, at once and
    /// permanently, with no waiting time after closing.
    /// </summary>
    private static readonly TicketSystemRules Immediate =
        new()
        {
            SystemName = "Helpdesk",
            ReceiptPrefix = "helpdesk",
            DisplayInstance = instance => instance,
            BatchSize = 1,
            SignsInWithBrowser = false,
            RunsJobs = false,
            DescribeRequest = (_, first, _) => $"DELETE /tickets/{first}"
        };

    [Fact]
    public void ZendesksRules_GiveTheSentencesItAlwaysShowed()
    {
        var rules = ZendeskDeletionRules.Rules;

        Assert.Equal("Zendesk", rules.SystemName);
        Assert.Equal(100, rules.BatchSize);
        Assert.Equal("example.zendesk.com", rules.DisplayInstance("example"));
        Assert.Equal("24 hours", rules.BlockedWindowText);
        Assert.Equal(
            "Zendesk keeps deleted tickets for 30 days, except old closed " +
            "tickets that Zendesk has archived: those cannot be restored from " +
            "Zendesk's deleted tickets at all.",
            rules.RecoveryNote);
        Assert.Equal(
            "Zendesk keeps deleted tickets for 30 days.",
            rules.ShortRecoveryNote);
        Assert.Equal("Check the deleted tickets in Zendesk.", rules.CheckAdvice);
        Assert.Equal("Zendesk's deleted tickets", rules.CheckPlace);
        Assert.Equal(
            "DELETE /api/v2/tickets/destroy_many.json?ids=<3 ticket ID(s): 1..9>",
            rules.DescribeRequest(3, 1, 9));
        Assert.Equal(
            "May return nothing: Zendesk does not always search by domain.",
            rules.DomainSearchNote);
    }

    [Fact]
    public void WithoutAWaitingTime_ClosedTicketsOfAnyAge_AreSelected_AndNoneIsArchived()
    {
        var tickets =
            new[]
            {
                Ticket(1, closedAgo: TimeSpan.FromMinutes(1)),
                Ticket(2, closedAgo: TimeSpan.FromDays(400)),
                new TicketSummary(3, "closed", "Example subject 3", 1, 10, null, null),
                Ticket(4, status: "open")
            };

        var preview = Build(tickets, Immediate);

        Assert.Equal([1L, 2L, 3L], Selected(preview));
        Assert.All(preview.Tickets.Take(3), ticket => Assert.Null(ticket.Flag));
        Assert.DoesNotContain(preview.Tickets, ticket => ticket.LikelyArchived);
        Assert.Equal(DeletionPreviewFlag.NotClosed, preview.Tickets[3].Flag);
    }

    [Fact]
    public void ZendesksWaitingTime_StillHoldsBackRecentlyClosedTickets()
    {
        var preview =
            Build(
                [
                    Ticket(1, closedAgo: TimeSpan.FromMinutes(1)),
                    Ticket(2, closedAgo: TimeSpan.FromDays(400))
                ],
                ZendeskDeletionRules.Rules);

        Assert.Equal([2L], Selected(preview));
        Assert.Equal(DeletionPreviewFlag.ClosedWithin24Hours, preview.Tickets[0].Flag);
        Assert.True(preview.Tickets[1].LikelyArchived);
    }

    [Fact]
    public void ThePlan_UsesTheSystemsRequestSize()
    {
        var plan = PlanFor(Immediate, 1, 2, 3);

        Assert.Equal(1, plan.BatchSize);
        Assert.Equal(3, plan.Batches.Count);
        Assert.Equal("helpdesk.example", plan.DisplayInstance);
    }

    [Fact]
    public async Task ASystemThatDeletesAtOnce_IsDeletedTicketByTicket_WithoutPolling()
    {
        var deleter = new ImmediateDeleter();
        var plan = PlanFor(Immediate, 1, 2, 3);

        var run =
            await Executor(deleter)
                .ExecuteAsync(plan, new DeletionConfirmation(3, null));

        Assert.True(run.Outcome.IsComplete);
        Assert.Equal([1L, 2L, 3L], deleter.Sent);
        Assert.StartsWith("helpdesk-delete-", Path.GetFileName(run.ReceiptPath));
    }

    [Fact]
    public async Task AnUnclearDeletion_StopsTheRun_AndNamesTheSystem()
    {
        var deleter = new ImmediateDeleter { Unclear = id => id == 2 };
        var plan = PlanFor(Immediate, 1, 2, 3);

        var run =
            await Executor(deleter)
                .ExecuteAsync(plan, new DeletionConfirmation(3, null));

        var results = run.Outcome.Results.ToDictionary(result => result.Id);

        Assert.Equal(TicketDeletionState.Deleted, results[1].State);
        Assert.Equal(TicketDeletionState.Unknown, results[2].State);
        Assert.EndsWith("Check the tickets in Helpdesk.", results[2].Detail);
        Assert.Equal(TicketDeletionState.NotAttempted, results[3].State);
        Assert.Equal([1L, 2L], deleter.Sent);
    }

    [Fact]
    public void ASystemThatCannotRestore_SaysSo()
    {
        Assert.False(Immediate.CanRestore);
        Assert.Contains("immediately and permanently", Immediate.RecoveryNote);
        Assert.Contains("cannot be restored", Immediate.ShortRecoveryNote);
        Assert.Equal("the tickets in Helpdesk", Immediate.CheckPlace);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zendesk")]
    [InlineData("../x")]
    [InlineData("zen desk")]
    public void AReceiptPrefix_IsLowerCaseLettersAndDigitsOnly(string prefix)
    {
        var store = new DeletionReceiptStore(_receipts);

        Assert.Throws<ArgumentException>(
            () => store.PathFor(Guid.NewGuid(), Now, prefix));
    }

    private static DeletionPreview Build(
        IEnumerable<TicketSummary> tickets,
        TicketSystemRules rules)
    {
        var list = tickets.ToArray();

        return DeletionPreviewBuilder.Build(
            new TicketSearchResult(list, list.Length, 0, true, null),
            null,
            null,
            null,
            Now,
            rules);
    }

    private static long[] Selected(DeletionPreview preview) =>
        preview.Tickets
            .Where(ticket => ticket.Selected)
            .Select(ticket => ticket.Id)
            .ToArray();

    private static DeletionPlan PlanFor(
        TicketSystemRules rules,
        params long[] ids)
    {
        var preview =
            new DeletionPreviewResult(
                Operator,
                "example query",
                Build(ids.Select(id => Ticket(id)), rules));

        return DeletionPlan.Create(
            preview,
            new DeletionSelection(preview.Preview),
            "helpdesk.example",
            Now);
    }

    private DeletionExecutor Executor(ITicketDeleter deleter) =>
        new(
            deleter,
            new DeletionReceiptStore(_receipts),
            new FixedClock(),
            (_, _) => Task.CompletedTask);

    /// <summary>
    /// A system that answers each deletion with its outcome: the job it
    /// returns has already finished.
    /// </summary>
    private sealed class ImmediateDeleter : ITicketDeleter
    {
        public Func<long, bool> Unclear { get; init; } = _ => false;

        public List<long> Sent { get; } = [];

        public Task<DeleteJob> StartDeleteAsync(
            IReadOnlyList<long> ticketIds,
            CancellationToken cancellationToken)
        {
            var id = Assert.Single(ticketIds);

            Sent.Add(id);

            if (Unclear(id))
            {
                throw new DeletionRequestException(
                    DeletionRequestFailure.Unclear,
                    nameof(TaskCanceledException),
                    "The request timed out.");
            }

            return Task.FromResult(
                new DeleteJob(
                    "ticket-" + id,
                    DeleteJobState.Completed,
                    "deleted",
                    1,
                    1,
                    null,
                    [new DeleteJobResult(id, true, null)]));
        }

        public Task<DeleteJob> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "A finished job is never polled.");
    }
}
