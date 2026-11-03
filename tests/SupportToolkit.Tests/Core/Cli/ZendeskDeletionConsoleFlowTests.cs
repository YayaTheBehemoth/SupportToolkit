using SupportToolkit.Cli.Commands;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Tests.Modules.ZendeskBulkDelete;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Cli;

public class ZendeskDeletionConsoleFlowTests : IDisposable
{
    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    private static ZendeskBulkDeleteOptions Options(
        ZendeskBulkDeleteMode mode,
        long[]? include = null,
        long[]? exclude = null) =>
        new(
            new ZendeskSearchCriteria(FreeText: "example"),
            mode,
            include,
            exclude);

    private async Task<(int Exit, string Output, FakeDeleteApi Api)> Run(
        DeletionPreviewResult preview,
        ZendeskBulkDeleteOptions options,
        string input,
        FakeDeleteApi? api = null,
        bool withExecutor = true)
    {
        api ??= new FakeDeleteApi();

        var output = new StringWriter();

        var flow =
            new ZendeskDeletionConsoleFlow(
                withExecutor ? Executor(api, _receipts) : null,
                new StringReader(input),
                output,
                new FixedClock());

        var exit = await flow.RunAsync(preview, options, "example");

        return (exit, output.ToString(), api);
    }

    [Fact]
    public async Task DryRun_ShowsExactlyWhatWouldBeSent_AndSendsNothing()
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(250)),
                Options(ZendeskBulkDeleteMode.DryRun),
                "");

        Assert.Equal(0, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("DRY RUN", output);
        Assert.Contains("1. DELETE /api/v2/tickets/destroy_many.json", output);
        Assert.Contains("3. DELETE", output);
        Assert.Contains("1..100", output);
        Assert.Contains("201..250", output);
        Assert.Empty(Directory.GetFiles(_receipts));
    }

    [Fact]
    public async Task Delete_WithTheRightCount_DeletesAndWritesAReceipt()
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(3)),
                Options(ZendeskBulkDeleteMode.Delete),
                "3\n");

        Assert.Equal(0, exit);
        Assert.Equal([1L, 2L, 3L], api.DeletedBatches.Single());
        Assert.Contains("Deleted:       3", output);
        Assert.Contains("Receipt:", output);
        Assert.Single(Directory.GetFiles(_receipts));
    }

    [Theory]
    [InlineData("2\n")]
    [InlineData("\n")]
    [InlineData("yes\n")]
    [InlineData("")]
    [InlineData("3 tickets\n")]
    public async Task Delete_WithAnyOtherAnswer_CancelsAndDeletesNothing(
        string input)
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(3)),
                Options(ZendeskBulkDeleteMode.Delete),
                input);

        Assert.Equal(1, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("Cancelled. Nothing was deleted.", output);
        Assert.Empty(Directory.GetFiles(_receipts));
    }

    [Fact]
    public async Task LikelyArchivedTickets_AreWarnedAbout_BeforeTheFirstConfirmation()
    {
        var (exit, output, api) =
            await Run(
                Preview(
                [
                    Ticket(1, closedAgo: TimeSpan.FromDays(200)),
                    Ticket(2, closedAgo: TimeSpan.FromDays(10))
                ]),
                Options(ZendeskBulkDeleteMode.Delete),
                "2\n");

        var warning = output.IndexOf("probably archived", StringComparison.Ordinal);
        var confirmation = output.IndexOf("Confirmation 1 of", StringComparison.Ordinal);

        Assert.True(warning >= 0, "The warning is missing.");
        Assert.True(warning < confirmation, "The warning must come before the first confirmation.");

        // The warning names only the archived ticket, and says how to restore it.
        var text = output[warning..confirmation];

        Assert.Contains("#1", text);
        Assert.DoesNotContain("#2", text);
        Assert.Contains("using the receipt", text);

        // It informs; it does not stop a confirmed deletion.
        Assert.Equal(0, exit);
        Assert.Equal([1L, 2L], api.DeletedBatches.Single());
    }

    [Fact]
    public async Task WhenNothingIsLikelyArchived_NoArchiveWarningIsShown()
    {
        var (_, output, _) =
            await Run(
                Preview(Closed(3)),
                Options(ZendeskBulkDeleteMode.Delete),
                "3\n");

        Assert.DoesNotContain("probably archived", output);
    }
    [Fact]
    public async Task NonClosedTickets_AreListedExplicitly_AndNeedASecondConfirmation()
    {
        var preview = Preview([Ticket(1), Ticket(2), Ticket(3, "open")]);

        var (exit, output, api) =
            await Run(
                preview,
                Options(ZendeskBulkDeleteMode.Delete, include: [3]),
                "3\n1\n");

        Assert.Equal(0, exit);
        Assert.Contains("1 of these ticket(s) are NOT closed", output);
        Assert.Contains("#3  open", output);
        Assert.Contains("Confirmation 2 of 2", output);
        Assert.Equal([1L, 2L, 3L], api.DeletedBatches.Single());
    }

    [Fact]
    public async Task AWrongSecondConfirmation_CancelsBeforeAnythingIsSent()
    {
        var preview = Preview([Ticket(1), Ticket(2), Ticket(3, "open")]);

        var (exit, output, api) =
            await Run(
                preview,
                Options(ZendeskBulkDeleteMode.Delete, include: [3]),
                "3\n9\n");

        Assert.Equal(1, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("Cancelled", output);
    }

    [Fact]
    public async Task AllClosed_NeedsOnlyTheFirstConfirmation()
    {
        var (_, output, _) =
            await Run(
                Preview(Closed(2)),
                Options(ZendeskBulkDeleteMode.Delete),
                "2\n");

        Assert.Contains("Confirmation 1 of 1", output);
        Assert.DoesNotContain("Confirmation 2", output);
    }

    [Fact]
    public async Task Exclude_RemovesTicketsFromTheSelection()
    {
        var (exit, _, api) =
            await Run(
                Preview(Closed(4)),
                Options(ZendeskBulkDeleteMode.Delete, exclude: [2, 3]),
                "2\n");

        Assert.Equal(0, exit);
        Assert.Equal([1L, 4L], api.DeletedBatches.Single());
    }

    [Fact]
    public async Task NothingSelected_DeletesNothing_AndSaysHowToAddTickets()
    {
        var (exit, output, api) =
            await Run(
                Preview([Ticket(1, "solved"), Ticket(2, "open")]),
                Options(ZendeskBulkDeleteMode.Delete),
                "");

        Assert.Equal(0, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("Nothing is selected", output);
        Assert.Contains("--include", output);
    }

    [Fact]
    public async Task ATicketClosedLessThan24HoursAgo_CannotBeIncluded()
    {
        var preview =
            Preview([Ticket(1), Ticket(2, closedAgo: TimeSpan.FromHours(2))]);

        var (exit, output, api) =
            await Run(
                preview,
                Options(ZendeskBulkDeleteMode.Delete, include: [2]),
                "1\n");

        Assert.Equal(1, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("Selection error", output);
        Assert.Contains("#2", output);
    }

    [Fact]
    public async Task AnIdNotInTheSearch_IsASelectionError()
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(2)),
                Options(ZendeskBulkDeleteMode.Delete, include: [999]),
                "2\n");

        Assert.Equal(1, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("Not in this search result: #999", output);
    }

    [Fact]
    public async Task AnIncompleteSearch_IsNeverDeleted()
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(3), complete: false),
                Options(ZendeskBulkDeleteMode.Delete),
                "3\n");

        Assert.Equal(2, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("incomplete", output);
    }

    [Fact]
    public async Task WithoutAnExecutor_DeletionIsRefused()
    {
        var (exit, output, api) =
            await Run(
                Preview(Closed(2)),
                Options(ZendeskBulkDeleteMode.Delete),
                "2\n",
                withExecutor: false);

        Assert.Equal(1, exit);
        Assert.Empty(api.Calls);
        Assert.Contains("not enabled", output);
    }

    [Fact]
    public async Task AnyTicketNotConfirmedDeleted_MakesTheExitCodeTwo_AndIsListed()
    {
        var api = new FakeDeleteApi { FailTicket = id => id == 2 };

        var (exit, output, _) =
            await Run(
                Preview(Closed(3)),
                Options(ZendeskBulkDeleteMode.Delete),
                "3\n",
                api);

        Assert.Equal(2, exit);
        Assert.Contains("Deleted:       2", output);
        Assert.Contains("Failed:        1", output);
        Assert.Contains("#2", output);
    }

    [Fact]
    public async Task UnknownTickets_AreStatedAsUnknown_NeverAsDeleted()
    {
        var api =
            new FakeDeleteApi
            {
                DeleteOverride = _ =>
                    FakeDeleteApi.Json(
                        System.Net.HttpStatusCode.InternalServerError,
                        "{}")
            };

        var (exit, output, _) =
            await Run(
                Preview(Closed(2)),
                Options(ZendeskBulkDeleteMode.Delete),
                "2\n",
                api);

        Assert.Equal(2, exit);
        Assert.Contains("Unknown:       2", output);
        Assert.Contains("Deleted:       0", output);
        Assert.Contains("may or may not", output);
    }

    [Fact]
    public async Task ThePlanListsTheExactIds_BeforeAnythingIsAsked()
    {
        var (_, output, _) =
            await Run(
                Preview(Closed(12)),
                Options(ZendeskBulkDeleteMode.DryRun),
                "");

        Assert.Contains("Exact ticket IDs (nothing else will be sent):", output);
        Assert.Contains("#1  #2  #3", output);
        Assert.Contains("#11  #12", output);
        Assert.Contains("12 in 1 request(s)", output);
    }
}
