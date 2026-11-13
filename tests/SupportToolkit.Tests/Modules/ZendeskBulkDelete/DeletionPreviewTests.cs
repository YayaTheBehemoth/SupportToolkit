using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Reporting;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Tests.Modules.ZendeskBulkDelete;

public class DeletionPreviewTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static TicketSummary Ticket(
        long id,
        string? status = "closed",
        long? requester = 1,
        long? organization = 10,
        TimeSpan? closedAgo = null,
        bool noUpdated = false) =>
        new(
            id,
            status,
            $"Example subject {id}",
            requester,
            organization,
            Now.AddDays(-30),
            noUpdated ? null : Now - (closedAgo ?? TimeSpan.FromDays(10)));

    private static TicketSearchResult Result(
        params TicketSummary[] tickets) =>
        new(tickets, tickets.Length, 0, true, null);

    private static readonly Dictionary<long, string> Orgs =
        new() { [10] = "Example Org", [11] = "Other Org" };

    private static readonly Dictionary<long, string> Emails =
        new()
        {
            [1] = "a@Example.com",
            [2] = "b@example.com",
            [3] = "c@stray.test"
        };

    [Fact]
    public void ClosedTickets_AreSelectedByDefault()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1), Ticket(2)),
                Orgs,
                Emails,
                null,
                Now);

        Assert.Equal(2, preview.Total);
        Assert.Equal(2, preview.SelectedCount);
        Assert.All(preview.Tickets, ticket => Assert.True(ticket.Selected));
        Assert.Empty(preview.Exceptions);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("pending")]
    [InlineData("solved")]
    [InlineData("new")]
    [InlineData("hold")]
    public void NonClosedTickets_AreExceptions_AndNotSelected(string status)
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1), Ticket(2, status)),
                Orgs,
                Emails,
                null,
                Now);

        var exception = Assert.Single(preview.Exceptions);

        Assert.Equal(2, exception.Id);
        Assert.Equal(DeletionPreviewFlag.NotClosed, exception.Flag);
        Assert.False(exception.Selected);
        Assert.Equal(1, preview.SelectedCount);
    }

    [Fact]
    public void AMissingStatus_IsNotClosed_SoNotSelected()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1, status: null)),
                Orgs,
                Emails,
                null,
                Now);

        var ticket = Assert.Single(preview.Tickets);

        Assert.Equal("(unknown)", ticket.Status);
        Assert.False(ticket.Selected);
    }

    [Fact]
    public void ClosedLessThan24HoursAgo_IsFlagged_AndNotSelected()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(
                    Ticket(1, closedAgo: TimeSpan.FromHours(23)),
                    Ticket(2, closedAgo: TimeSpan.FromHours(25))),
                Orgs,
                Emails,
                null,
                Now);

        var recent = Assert.Single(preview.Exceptions);

        Assert.Equal(1, recent.Id);
        Assert.Equal(DeletionPreviewFlag.ClosedWithin24Hours, recent.Flag);

        Assert.True(preview.Tickets.Single(t => t.Id == 2).Selected);
    }

    [Fact]
    public void AClosedTicketWithoutATime_IsTreatedAsTooRecent()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1, noUpdated: true)),
                Orgs,
                Emails,
                null,
                Now);

        var ticket = Assert.Single(preview.Tickets);

        Assert.False(ticket.Selected);
        Assert.Equal(DeletionPreviewFlag.ClosedTimeUnknown, ticket.Flag);
    }

    [Fact]
    public void Groups_ShowOrganizationAndRequesterDomain_SoStraysAreVisible()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(
                    Ticket(1, requester: 1, organization: 10),
                    Ticket(2, requester: 2, organization: 10),
                    Ticket(3, requester: 3, organization: 11),
                    Ticket(4, requester: null, organization: null)),
                Orgs,
                Emails,
                null,
                Now);

        Assert.Equal(
            [("Example Org", 2), ("(no organization)", 1), ("Other Org", 1)],
            preview.ByOrganization.Select(g => (g.Name, g.Count)));

        // The stray domain stands out next to the expected one, and
        // domains are compared case-insensitively.
        Assert.Equal(
            [("example.com", 2), ("(no requester)", 1), ("stray.test", 1)],
            preview.ByRequesterDomain.Select(g => (g.Name, g.Count)));

        Assert.Equal(
            [("closed", 4)],
            preview.StatusCounts.Select(g => (g.Name, g.Count)));
    }

    [Fact]
    public void FailedLookups_MakeTheNamesUnknown_NeverGuessed()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1), Ticket(2, organization: null)),
                organizationNames: null,
                userEmails: null,
                directoryNote: "lookups failed",
                Now);

        Assert.Equal("lookups failed", preview.DirectoryNote);

        Assert.Equal(
            "(unknown)",
            preview.Tickets.Single(t => t.Id == 1).Organization);

        Assert.Equal(
            "(unknown)",
            preview.Tickets.Single(t => t.Id == 1).RequesterDomain);

        // A ticket with no organization at all is still "none", not unknown.
        Assert.Equal(
            "(no organization)",
            preview.Tickets.Single(t => t.Id == 2).Organization);
    }

    [Fact]
    public void AnIdZendeskDidNotReturn_IsUnknown()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1, requester: 99, organization: 98)),
                Orgs,
                Emails,
                null,
                Now);

        var ticket = Assert.Single(preview.Tickets);

        Assert.Equal("(unknown)", ticket.Organization);
        Assert.Equal("(unknown)", ticket.RequesterDomain);
    }

    [Fact]
    public void AnIncompleteSearch_StaysIncomplete_InThePreview()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                new TicketSearchResult(
                    [Ticket(1)],
                    1500,
                    2,
                    false,
                    "too many matches"),
                Orgs,
                Emails,
                null,
                Now);

        Assert.False(preview.IsSearchComplete);
        Assert.Equal("too many matches", preview.SearchIncompleteReason);
        Assert.Equal(2, preview.DuplicateCount);
    }

    [Fact]
    public void Reporter_StatesAnIncompleteSearchLoudly_AndThatNothingIsDeleted()
    {
        var incomplete =
            DeletionPreviewBuilder.Build(
                new TicketSearchResult(
                    [Ticket(1), Ticket(2, "open")],
                    1500,
                    0,
                    false,
                    "too many matches"),
                Orgs,
                Emails,
                "names unavailable",
                Now);

        var output = new StringWriter();

        ConsoleDeletionPreviewReporter.Write(
            output,
            new DeletionPreviewResult(
                new DeletionOperator(7, "Example Operator", "op@example.com", "agent"),
                "type:ticket example",
                incomplete));

        var text = output.ToString();

        Assert.Contains("nothing is deleted", text);
        Assert.Contains("INCOMPLETE SEARCH", text);
        Assert.Contains("too many matches", text);
        Assert.Contains("NOTE: names unavailable", text);
        Assert.Contains("Example Operator", text);
        Assert.Contains("[x] #1", text);
        Assert.Contains("[ ] #2", text);
        Assert.Contains("not closed", text);
    }

    [Fact]
    public void Reporter_OmitsTheIncompleteBanner_WhenTheSearchIsComplete()
    {
        var preview =
            DeletionPreviewBuilder.Build(
                Result(Ticket(1)),
                Orgs,
                Emails,
                null,
                Now);

        var output = new StringWriter();

        ConsoleDeletionPreviewReporter.Write(
            output,
            new DeletionPreviewResult(
                new DeletionOperator(7, null, null, null),
                "type:ticket example",
                preview));

        Assert.DoesNotContain("INCOMPLETE", output.ToString());
    }

    [Fact]
    public async Task Workflow_SignsIn_Searches_AndBuildsThePreview()
    {
        var server = new Server();
        var signIns = 0;

        var workflow = CreateWorkflow(server, () => signIns++);

        var operatorUser = await workflow.SignInAsync();

        Assert.Equal(1, signIns);
        Assert.Equal(7, operatorUser.Id);

        var result =
            await workflow.PreviewAsync(
                operatorUser,
                new TicketSearchCriteria(Organization: "Example Org"));

        Assert.Equal(
            "type:ticket organization:\"Example Org\"",
            result.Query);

        Assert.Equal(3, result.Preview.Total);
        Assert.Equal(2, result.Preview.SelectedCount);
        Assert.Equal("Example Org", result.Preview.Tickets[0].Organization);
        Assert.Equal("example.com", result.Preview.Tickets[0].RequesterDomain);
        Assert.Null(result.Preview.DirectoryNote);
    }

    [Fact]
    public async Task Workflow_NeverIssuesAnythingButGets()
    {
        var server = new Server();

        var workflow = CreateWorkflow(server);

        await workflow.PreviewAsync(
            new DeletionOperator(7, null, null, null),
            new TicketSearchCriteria(FreeText: "example"));

        Assert.NotEmpty(server.Methods);
        Assert.All(server.Methods, method => Assert.Equal("GET", method));
    }

    [Fact]
    public async Task Workflow_AFailedLookup_DoesNotFailThePreview_ButSaysSo()
    {
        var server = new Server(failLookups: HttpStatusCode.InternalServerError);

        var workflow = CreateWorkflow(server);

        var result =
            await workflow.PreviewAsync(
                new DeletionOperator(7, null, null, null),
                new TicketSearchCriteria(FreeText: "example"));

        Assert.Equal(3, result.Preview.Total);
        Assert.Contains("Organization names", result.Preview.DirectoryNote);
        Assert.Contains("Requester emails", result.Preview.DirectoryNote);
        Assert.All(
            result.Preview.Tickets,
            ticket => Assert.Equal("(unknown)", ticket.Organization));
    }

    [Fact]
    public async Task Workflow_AnUnauthorizedLookup_IsNotSwallowed()
    {
        var server = new Server(failLookups: HttpStatusCode.Unauthorized);

        var workflow = CreateWorkflow(server);

        var exception =
            await Assert.ThrowsAsync<ZendeskApiException>(
                () => workflow.PreviewAsync(
                    new DeletionOperator(7, null, null, null),
                    new TicketSearchCriteria(FreeText: "example")));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task Workflow_AFailedSearch_FailsThePreview()
    {
        var server = new Server(failSearch: HttpStatusCode.Forbidden);

        var workflow = CreateWorkflow(server);

        await Assert.ThrowsAsync<ZendeskApiException>(
            () => workflow.PreviewAsync(
                new DeletionOperator(7, null, null, null),
                new TicketSearchCriteria(FreeText: "example")));
    }

    [Fact]
    public async Task Workflow_RefusesASearchWithNoCriterion()
    {
        var workflow = CreateWorkflow(new Server());

        await Assert.ThrowsAsync<ArgumentException>(
            () => workflow.PreviewAsync(
                new DeletionOperator(7, null, null, null),
                new TicketSearchCriteria()));
    }

    private static ZendeskBulkDeleteWorkflow CreateWorkflow(
        Server server,
        Action? onSignIn = null)
    {
        var client =
            new ZendeskDeletionApiClient(
                new HttpClient(server)
                {
                    BaseAddress = new Uri("https://example.zendesk.com/")
                },
                ZendeskDeletionOptions.Create(
                    "example",
                    "example-public-client"));

        return new ZendeskBulkDeleteWorkflow(
            new ZendeskTicketDirectory(
                _ =>
                {
                    onSignIn?.Invoke();
                    return Task.CompletedTask;
                },
                new ZendeskTicketSearchProvider(client),
                new ZendeskDirectoryProvider(client)),
            new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>
    /// Three closed-or-open tickets for one organization and requester.
    /// </summary>
    private sealed class Server : HttpMessageHandler
    {
        private readonly HttpStatusCode? _failLookups;
        private readonly HttpStatusCode? _failSearch;

        public Server(
            HttpStatusCode? failLookups = null,
            HttpStatusCode? failSearch = null)
        {
            _failLookups = failLookups;
            _failSearch = failSearch;
        }

        public List<string> Methods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v2/search.json")
            {
                return Task.FromResult(
                    _failSearch is { } status
                        ? Respond(status, "{}")
                        : Respond(
                            HttpStatusCode.OK,
                            """
                            {"count":3,"next_page":null,"results":[
                              {"id":1,"result_type":"ticket","status":"closed",
                               "requester_id":1,"organization_id":10,
                               "updated_at":"2026-10-01T00:00:00Z"},
                              {"id":2,"result_type":"ticket","status":"closed",
                               "requester_id":1,"organization_id":10,
                               "updated_at":"2026-10-01T00:00:00Z"},
                              {"id":3,"result_type":"ticket","status":"open",
                               "requester_id":1,"organization_id":10,
                               "updated_at":"2026-11-01T00:00:00Z"}]}
                            """));
            }

            if (path == "/api/v2/users/me.json")
            {
                return Task.FromResult(
                    Respond(
                        HttpStatusCode.OK,
                        """{"user":{"id":7,"name":"Example Operator"}}"""));
            }

            if (_failLookups is { } failure)
            {
                return Task.FromResult(Respond(failure, "{}"));
            }

            return Task.FromResult(
                path == "/api/v2/users/show_many.json"
                    ? Respond(
                        HttpStatusCode.OK,
                        """{"users":[{"id":1,"email":"a@example.com"}]}""")
                    : Respond(
                        HttpStatusCode.OK,
                        """{"organizations":[{"id":10,"name":"Example Org"}]}"""));
        }

        private static HttpResponseMessage Respond(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
    }
}
