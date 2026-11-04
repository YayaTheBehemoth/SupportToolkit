using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Ui.Web;
using static SupportToolkit.Tests.Modules.ZendeskBulkDelete.DeletionTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

/// <summary>
/// The web session runs sign-in, search and deletion in the background and
/// relays the rules of BulkDeleteReviewSession; it adds none of its own.
/// </summary>
public class BulkDeleteWebSessionTests : IDisposable
{
    private static readonly TicketSearchCriteria Criteria =
        new(Organization: "Example Org");

    private readonly string _receipts = NewTempDirectory();

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    private BulkDeleteWebSession NewWeb(
        FakeDeleteApi? api = null,
        Func<CancellationToken, Task<DeletionOperator>>? signIn = null,
        int tickets = 3)
    {
        var list = Closed(tickets).ToArray();

        return new BulkDeleteWebSession(
            _ => Task.FromResult(
                new BulkDeleteAvailability(true, "example", null)),
            _ => Task.FromResult(
                new BulkDeleteConnection(
                    new BulkDeleteReviewSession(
                        "example",
                        signIn ?? (_ => Task.FromResult(Operator)),
                        (_, _, _) => Task.FromResult(Preview(list)),
                        Executor(api ?? new FakeDeleteApi(), _receipts),
                        new FixedClock()),
                    null)));
    }

    private static async Task WaitUntilIdle(BulkDeleteWebSession web)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (web.Busy is not null)
        {
            Assert.True(DateTime.UtcNow < deadline, "It did not finish.");
            await Task.Delay(20);
        }
    }

    private static async Task SignedInAndSearched(BulkDeleteWebSession web)
    {
        Assert.True(web.StartSignIn());
        await WaitUntilIdle(web);

        Assert.True(web.StartSearch(Criteria));
        await WaitUntilIdle(web);
    }

    [Fact]
    public async Task SigningIn_AndSearching_RunInTheBackground_AndTellThePage()
    {
        var web = NewWeb();
        var changes = 0;

        web.Changed += () => Interlocked.Increment(ref changes);

        await SignedInAndSearched(web);

        Assert.Null(web.Error);
        Assert.Equal(BulkDeleteStage.Previewed, web.Session!.Stage);
        Assert.Equal(3, web.Session.SelectedCount);
        Assert.True(changes >= 4);
    }

    [Fact]
    public async Task AFailedSignIn_ShowsWhy_AndLeavesNothingSignedIn()
    {
        var web =
            NewWeb(
                signIn: _ => throw new SignInRequiredException(
                    "The sign-in timed out."));

        web.StartSignIn();
        await WaitUntilIdle(web);

        Assert.Contains("sign-in did not complete", web.Error);
        Assert.True(web.NeedsSignIn);
        Assert.Equal(BulkDeleteStage.SignedOut, web.Session!.Stage);
    }

    [Fact]
    public async Task AnEmptySearch_ExplainsItself_AndSearchesNothing()
    {
        var web = NewWeb();

        web.StartSignIn();
        await WaitUntilIdle(web);

        web.StartSearch(new TicketSearchCriteria());
        await WaitUntilIdle(web);

        Assert.NotNull(web.SearchProblem);
        Assert.Equal(BulkDeleteStage.SignedIn, web.Session!.Stage);
    }

    [Fact]
    public async Task ASecondAction_WhileOneIsRunning_IsRefused()
    {
        var gate = new TaskCompletionSource();
        var web =
            NewWeb(
                signIn: async _ =>
                {
                    await gate.Task;
                    return Operator;
                });

        Assert.True(web.StartSignIn());
        Assert.NotNull(web.Busy);
        Assert.False(web.StartSignIn());

        gate.SetResult();
        await WaitUntilIdle(web);

        Assert.Equal(BulkDeleteStage.SignedIn, web.Session!.Stage);
    }

    [Fact]
    public async Task ADeletion_RunsToTheEnd_WithoutAPageAttached()
    {
        var api = new FakeDeleteApi();
        var web = NewWeb(api);

        await SignedInAndSearched(web);

        web.PreparePlan();

        Assert.True(web.StartDelete(new DeletionConfirmation(3, null)));
        await WaitUntilIdle(web);

        Assert.Null(web.Error);
        Assert.Equal(BulkDeleteStage.Finished, web.Session!.Stage);
        Assert.True(web.Session.Result!.IsClean);
        Assert.Equal([1L, 2L, 3L], api.DeletedBatches.Single());
    }

    [Fact]
    public async Task AWrongConfirmation_SendsNothing_AndSaysSo()
    {
        var api = new FakeDeleteApi();
        var web = NewWeb(api);

        await SignedInAndSearched(web);

        web.PreparePlan();
        web.StartDelete(new DeletionConfirmation(2, null));
        await WaitUntilIdle(web);

        Assert.Contains("do not match", web.Error);
        Assert.Empty(api.Calls);
        Assert.Equal(BulkDeleteStage.Planned, web.Session!.Stage);
    }

    [Fact]
    public async Task WhileDeleting_NothingElseCanStart_AndTheProfileCannotBeReset()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();

        var api =
            new FakeDeleteApi
            {
                PollsUntilDone = 2,
                OnRequest =
                    _ =>
                    {
                        started.TrySetResult();
                        release.Task.GetAwaiter().GetResult();
                    }
            };

        var web = NewWeb(api);

        await SignedInAndSearched(web);

        web.PreparePlan();
        web.StartDelete(new DeletionConfirmation(3, null));

        await started.Task;

        Assert.True(web.IsDeleting);
        Assert.False(web.StartSearch(Criteria));
        Assert.False(web.StartDelete(new DeletionConfirmation(3, null)));
        Assert.False(await web.ResetAsync());

        release.SetResult();
        await WaitUntilIdle(web);

        Assert.Equal(BulkDeleteStage.Finished, web.Session!.Stage);
    }

    [Fact]
    public async Task Exiting_CancelsADeletion_AndWaitsForItsReceipt()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var api =
            new FakeDeleteApi
            {
                PollsUntilDone = 50,
                OnRequest =
                    request =>
                    {
                        if (request.Method == HttpMethod.Get)
                        {
                            started.TrySetResult();
                            release.Task.GetAwaiter().GetResult();
                        }
                    }
            };

        var web = NewWeb(api);

        await SignedInAndSearched(web);

        web.PreparePlan();
        web.StartDelete(new DeletionConfirmation(3, null));

        await started.Task;

        var exiting = web.CancelAndWaitAsync(TimeSpan.FromSeconds(20));

        release.SetResult();
        await exiting;
        await WaitUntilIdle(web);

        Assert.Equal(BulkDeleteStage.Finished, web.Session!.Stage);
        Assert.NotNull(web.Session.CancelledMessage);
        Assert.Single(Directory.GetFiles(_receipts));
    }

    [Fact]
    public async Task AResetForgetsTheSignIn_SoANewProfileStartsClean()
    {
        var web = NewWeb();

        await SignedInAndSearched(web);

        Assert.True(await web.ResetAsync());
        Assert.Null(web.Session);
    }
}
