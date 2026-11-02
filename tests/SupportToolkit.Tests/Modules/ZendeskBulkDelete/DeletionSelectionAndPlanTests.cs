using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

public class DeletionSelectionAndPlanTests
{
    private static DeletionPreviewResult MixedPreview() =>
        Preview(
        [
            Ticket(1),
            Ticket(2),
            Ticket(3, "open"),
            Ticket(4, "solved"),
            Ticket(5, closedAgo: TimeSpan.FromHours(3))
        ]);

    [Fact]
    public void DefaultSelection_IsTheClosedTicketsOldEnough()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        Assert.Equal(
            [1L, 2L],
            selection.SelectedTickets.Select(ticket => ticket.Id));

        Assert.Empty(selection.SelectedNonClosed);
    }

    [Fact]
    public void ANonClosedTicket_CanBeAddedExplicitly_AndIsListedAsNonClosed()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        selection.Select([3, 4]);

        Assert.Equal(4, selection.Count);

        Assert.Equal(
            [3L, 4L],
            selection.SelectedNonClosed.Select(ticket => ticket.Id));
    }

    [Fact]
    public void ATicketClosedLessThan24HoursAgo_CanNeverBeSelected()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        var exception =
            Assert.Throws<ArgumentException>(() => selection.Select([5]));

        Assert.Contains("#5", exception.Message);
        Assert.Contains("24 hours", exception.Message);
        Assert.False(selection.IsSelected(5));
    }

    [Fact]
    public void Changes_AreAllOrNothing()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        // 3 is fine, but 5 is blocked: nothing at all is added.
        Assert.Throws<ArgumentException>(() => selection.Select([3, 5]));

        Assert.False(selection.IsSelected(3));
        Assert.Equal(2, selection.Count);
    }

    [Fact]
    public void AnIdNotInTheSearch_IsRefused_NotIgnored()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        var exception =
            Assert.Throws<ArgumentException>(() => selection.Select([999]));

        Assert.Contains("#999", exception.Message);

        Assert.Throws<ArgumentException>(() => selection.Deselect([999]));
    }

    [Fact]
    public void Deselect_Reset_AndClear_Work()
    {
        var selection = new DeletionSelection(MixedPreview().Preview);

        selection.Select([3]);
        selection.Deselect([1]);

        Assert.Equal(
            [2L, 3L],
            selection.SelectedTickets.Select(ticket => ticket.Id));

        selection.Reset();

        Assert.Equal(
            [1L, 2L],
            selection.SelectedTickets.Select(ticket => ticket.Id));

        selection.Clear();

        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void Plan_FreezesTheSelectedIds_InOrder_AndRecordsWhoAndWhat()
    {
        var preview = MixedPreview();
        var selection = new DeletionSelection(preview.Preview);

        selection.Select([4]);

        var plan = DeletionPlan.Create(preview, selection, "example", Now);

        Assert.Equal([1L, 2L, 4L], plan.Tickets.Select(t => t.Id));
        Assert.Equal("example", plan.Instance);
        Assert.Equal(Operator.Id, plan.Operator.Id);
        Assert.Equal("type:ticket example", plan.Query);
        Assert.Equal(Now, plan.CreatedAt);
        Assert.Equal([4L], plan.NonClosed.Select(t => t.Id));

        // Changing the selection afterwards cannot change the plan.
        selection.Clear();

        Assert.Equal(3, plan.Tickets.Count);
    }

    [Fact]
    public void Plan_SplitsIntoBatchesOfAHundred()
    {
        var plan = Plan(Closed(250));

        Assert.Equal(
            [100, 100, 50],
            plan.Batches.Select(batch => batch.Count));

        Assert.Equal(
            Enumerable.Range(1, 250).Select(i => (long)i),
            plan.Batches.SelectMany(batch => batch));
    }

    [Fact]
    public void Plan_WithAnIncompleteSearch_IsRefused()
    {
        var preview = Preview(Closed(3), complete: false);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => DeletionPlan.Create(
                    preview,
                    new DeletionSelection(preview.Preview),
                    "example",
                    Now));

        Assert.Contains("incomplete", exception.Message);
        Assert.Contains("too many matches", exception.Message);
    }

    [Fact]
    public void Plan_WithNothingSelected_IsRefused()
    {
        var preview = Preview([Ticket(1, "open")]);

        Assert.Throws<InvalidOperationException>(
            () => DeletionPlan.Create(
                preview,
                new DeletionSelection(preview.Preview),
                "example",
                Now));
    }

    [Fact]
    public void Plan_RequiresAnInstance()
    {
        var preview = Preview(Closed(1));

        Assert.Throws<ArgumentException>(
            () => DeletionPlan.Create(
                preview,
                new DeletionSelection(preview.Preview),
                " ",
                Now));
    }
}
