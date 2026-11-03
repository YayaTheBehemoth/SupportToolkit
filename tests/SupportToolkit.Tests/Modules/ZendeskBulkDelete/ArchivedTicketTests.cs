using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

public class ArchivedTicketTests
{
    [Fact]
    public void OldClosedTickets_AreFlaggedLikelyArchived_RecentOnesAreNot()
    {
        var preview =
            Preview(
            [
                Ticket(1, closedAgo: TimeSpan.FromDays(200)),
                Ticket(2, closedAgo: TimeSpan.FromDays(120)),
                Ticket(3, closedAgo: TimeSpan.FromDays(119)),
                Ticket(4, status: "solved", closedAgo: TimeSpan.FromDays(400))
            ]).Preview;

        var archived =
            preview.Tickets.Where(t => t.LikelyArchived).Select(t => t.Id);

        Assert.Equal([1L, 2L], archived);
    }

    [Fact]
    public void UnknownCloseTime_IsNotClaimedToBeArchived()
    {
        Assert.False(
            DeletionPreviewBuilder.IsLikelyArchived("closed", null, Now));
    }

    [Fact]
    public void ThePlan_ListsOnlyTheSelectedLikelyArchivedTickets()
    {
        var plan =
            Plan(
                [
                    Ticket(1, closedAgo: TimeSpan.FromDays(200)),
                    Ticket(2, closedAgo: TimeSpan.FromDays(200)),
                    Ticket(3, closedAgo: TimeSpan.FromDays(10))
                ],
                1,
                3);

        // Closed tickets are selected by default; Plan() adds 1 and 3.
        Assert.All(plan.LikelyArchived, t => Assert.True(t.LikelyArchived));
        Assert.Contains(plan.LikelyArchived, t => t.Id == 1);
        Assert.DoesNotContain(plan.LikelyArchived, t => t.Id == 3);
    }

    [Fact]
    public void TheTicketText_IsCarriedIntoThePreview()
    {
        var search =
            new ZendeskSearchResult(
                [Ticket(1) with { Description = "Example ticket text" }],
                1, 0, true, null);

        var preview =
            DeletionPreviewBuilder.Build(search, null, null, null, Now);

        Assert.Equal("Example ticket text", preview.Tickets[0].Description);
    }
}

/// <summary>
/// The ticket text is customer content. It is shown on screen and kept in
/// memory only: it must never be written to the receipt.
/// </summary>
public class TicketTextPrivacyTests : IDisposable
{
    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    [Fact]
    public async Task TheTicketText_AndSubject_NeverReachTheReceipt()
    {
        const string Secret = "EXAMPLE-PRIVATE-TICKET-TEXT-7f3a";

        var tickets =
            Closed(3)
                .Select(t => t with { Description = Secret })
                .ToArray();

        var preview = Preview(tickets);

        // The text is in the preview, as the page needs it...
        Assert.All(preview.Preview.Tickets, t => Assert.Equal(Secret, t.Description));

        var plan = Plan(tickets);

        await Executor(new FakeDeleteApi(), _receipts)
            .ExecuteAsync(plan, new DeletionConfirmation(3, null));

        // ...but not in anything written to disk.
        var written =
            Directory.GetFiles(_receipts)
                .Select(File.ReadAllText)
                .ToArray();

        Assert.NotEmpty(written);

        foreach (var content in written)
        {
            Assert.DoesNotContain(Secret, content);
            Assert.DoesNotContain("Example subject", content);
        }
    }
}
