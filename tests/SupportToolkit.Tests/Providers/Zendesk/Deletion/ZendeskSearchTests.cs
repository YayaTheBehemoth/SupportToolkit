using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

public class ZendeskSearchTests
{
    [Fact]
    public void Query_IsAlwaysRestrictedToTickets_AndQuotesValues()
    {
        var query =
            ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(
                    Organization: "Example \"Org\" \\ Ltd",
                    RequesterEmail: "someone@example.com",
                    EmailDomain: "@Example.COM",
                    FreeText: "invoice"));

        Assert.StartsWith("type:ticket ", query);
        Assert.Contains("organization:\"Example \\\"Org\\\" \\\\ Ltd\"", query);
        Assert.Contains("requester:someone@example.com", query);
        Assert.DoesNotContain("requester:\"someone@example.com\"", query);
        Assert.Contains("requester:*@example.com", query);
        Assert.EndsWith("invoice", query);
    }

    [Fact]
    public void Query_SearchesTheSenderByName_AsAQuotedRequester()
    {
        var query =
            ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(SenderName: "Example \"Sender\" Name"));

        Assert.Equal(
            "type:ticket requester:\"Example \\\"Sender\\\" Name\"",
            query);
    }

    [Fact]
    public void ASenderAlone_IsEnoughOfACriterion()
    {
        Assert.True(
            new ZendeskSearchCriteria(SenderName: "Example Sender").HasAny);

        Assert.False(
            new ZendeskSearchCriteria(SenderName: "  ").HasAny);
    }

    [Fact]
    public void Query_RejectsControlCharactersInTheSender()
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(SenderName: "a\nb")));
    }

    [Fact]
    public void Query_WithoutAnyCriterion_IsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(new ZendeskSearchCriteria()));

        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(Organization: "  ")));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("two words@example.com")]
    [InlineData("a@example.com\nb")]
    public void Query_RejectsAnInvalidRequester(string email)
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(RequesterEmail: email)));
    }

    [Theory]
    [InlineData("exa mple.com")]
    [InlineData("example.com/path")]
    [InlineData(".example.com")]
    [InlineData("example*.com")]
    public void Query_RejectsAnInvalidDomain(string domain)
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(EmailDomain: domain)));
    }

    [Fact]
    public void Query_RejectsControlCharactersInTheOrganization()
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskSearchQuery.Build(
                new ZendeskSearchCriteria(Organization: "a\nb")));
    }

    [Fact]
    public async Task SinglePage_IsCompleteWhenTheCountMatches()
    {
        var server = new FakeZendesk(total: 3);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.True(result.IsComplete);
        Assert.Null(result.IncompleteReason);
        Assert.Equal(3, result.Tickets.Count);
        Assert.Equal(3, result.ReportedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task Search_SendsTheQueryOldestFirst_AtHundredPerPage()
    {
        var server = new FakeZendesk(total: 1);

        await Provider(server).SearchTicketsAsync(
            new ZendeskSearchCriteria(Organization: "Example Org"));

        var query = HttpUtility.ParseQueryString(server.Requests[0].Query);

        Assert.Equal("/api/v2/search.json", server.Requests[0].AbsolutePath);
        Assert.Equal(
            "type:ticket organization:\"Example Org\"",
            query["query"]);
        Assert.Equal("100", query["per_page"]);
        Assert.Equal("1", query["page"]);
        Assert.Equal("created_at", query["sort_by"]);
        Assert.Equal("asc", query["sort_order"]);
    }

    [Fact]
    public async Task Pagination_CollectsEveryPage()
    {
        var server = new FakeZendesk(total: 250);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.True(result.IsComplete);
        Assert.Equal(250, result.Tickets.Count);
        Assert.Equal(3, server.Requests.Count);
        Assert.Equal(
            Enumerable.Range(1, 250).Select(i => (long)i),
            result.Tickets.Select(ticket => ticket.Id));
    }

    [Fact]
    public async Task DuplicateTickets_AreRemoved_AndCounted()
    {
        // Zendesk can repeat a ticket across pages. 3 reported, 3 unique.
        var server =
            new FakeZendesk(
                total: 3,
                pages:
                [
                    [1, 2],
                    [2, 3]
                ]);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.True(result.IsComplete);
        Assert.Equal([1L, 2L, 3L], result.Tickets.Select(t => t.Id));
        Assert.Equal(1, result.DuplicateCount);
    }

    [Fact]
    public async Task CountDiffersFromTheUniqueTickets_IsIncomplete()
    {
        // 4 reported, but only 3 unique tickets arrived.
        var server =
            new FakeZendesk(
                total: 4,
                pages:
                [
                    [1, 2],
                    [2, 3]
                ]);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.False(result.IsComplete);
        Assert.Contains("4", result.IncompleteReason);
        Assert.Contains("3 unique", result.IncompleteReason);
        Assert.Equal(3, result.Tickets.Count);
    }

    [Fact]
    public async Task MoreThanTheLimit_IsIncomplete_NeverPresentedAsComplete()
    {
        var server = new FakeZendesk(total: 1500);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.False(result.IsComplete);
        Assert.Contains("1500", result.IncompleteReason);
        Assert.Contains("1000", result.IncompleteReason);
        Assert.Equal(1000, result.Tickets.Count);
        Assert.Equal(ZendeskTicketSearchProvider.MaxPages, server.Requests.Count);
    }

    [Fact]
    public async Task ExactlyTheLimit_IsComplete()
    {
        var server = new FakeZendesk(total: 1000);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.True(result.IsComplete);
        Assert.Equal(1000, result.Tickets.Count);
    }

    [Fact]
    public async Task MissingCount_IsIncomplete()
    {
        var server = new FakeZendesk(total: 2, reportCount: false);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.False(result.IsComplete);
        Assert.Contains("did not report", result.IncompleteReason);
    }

    [Fact]
    public async Task ResultsThatAreNotValidTickets_AreIncomplete()
    {
        var server =
            new FakeZendesk(
                total: 2,
                rawPages:
                [
                    """
                    {"count":2,"next_page":null,"results":[
                      {"id":1,"result_type":"ticket","status":"closed"},
                      {"id":2,"result_type":"user"}]}
                    """
                ]);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.False(result.IsComplete);
        Assert.Single(result.Tickets);
        Assert.Contains("not valid tickets", result.IncompleteReason);
    }

    [Fact]
    public async Task ZeroMatches_IsACompleteEmptyResult()
    {
        var server = new FakeZendesk(total: 0);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        Assert.True(result.IsComplete);
        Assert.Empty(result.Tickets);
    }

    [Fact]
    public async Task Forbidden_IsSurfacedClearly_NotAsAnEmptyResult()
    {
        var server = new FakeZendesk(total: 3, failWith: HttpStatusCode.Forbidden);

        var exception =
            await Assert.ThrowsAsync<ZendeskApiException>(
                () => Provider(server).SearchTicketsAsync(Criteria()));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("403", exception.Message);
        Assert.Contains("read", exception.Message);
    }

    [Fact]
    public async Task AFailedLaterPage_FailsTheWholeSearch_WithNoPartialList()
    {
        var server =
            new FakeZendesk(
                total: 250,
                failWith: HttpStatusCode.InternalServerError,
                failOnPage: 2);

        await Assert.ThrowsAsync<ZendeskApiException>(
            () => Provider(server).SearchTicketsAsync(Criteria()));
    }

    [Fact]
    public async Task Tickets_CarryTheirFieldsAndParsedDates()
    {
        var server =
            new FakeZendesk(
                total: 1,
                rawPages:
                [
                    """
                    {"count":1,"next_page":null,"results":[
                      {"id":9,"result_type":"ticket","status":"closed",
                       "subject":"Example subject","requester_id":11,
                       "organization_id":12,
                       "created_at":"2026-01-02T03:04:05Z",
                       "updated_at":"not a date"}]}
                    """
                ]);

        var result =
            await Provider(server).SearchTicketsAsync(Criteria());

        var ticket = Assert.Single(result.Tickets);

        Assert.Equal("closed", ticket.Status);
        Assert.Equal("Example subject", ticket.Subject);
        Assert.Equal(11, ticket.RequesterId);
        Assert.Equal(12, ticket.OrganizationId);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ticket.CreatedAt);

        // An unreadable date is unknown, not a crash and not "now".
        Assert.Null(ticket.UpdatedAt);
    }

    [Fact]
    public async Task CurrentUser_IsRead()
    {
        var server =
            new FakeZendesk(
                total: 0,
                userJson:
                    """{"user":{"id":7,"name":"Example Operator","email":"op@example.com","role":"agent"}}""");

        var operatorUser =
            await Directory(server).GetCurrentUserAsync();

        Assert.Equal(7, operatorUser.Id);
        Assert.Equal("Example Operator", operatorUser.Name);
        Assert.Equal("agent", operatorUser.Role);
    }

    [Fact]
    public async Task CurrentUser_WithoutAnId_Fails()
    {
        var server = new FakeZendesk(total: 0, userJson: """{"user":{}}""");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Directory(server).GetCurrentUserAsync());
    }

    [Fact]
    public async Task Lookups_AreBatchedAtOneHundred_AndMissingIdsStayAbsent()
    {
        var server = new FakeZendesk(total: 0);

        var emails =
            await Directory(server).GetUserEmailsAsync(
                Enumerable.Range(1, 150).Select(i => (long)i));

        // Two batches: 100 then 50.
        Assert.Equal(2, server.Requests.Count);

        Assert.Equal(
            100,
            HttpUtility.ParseQueryString(server.Requests[0].Query)["ids"]!
                .Split(',').Length);

        Assert.Equal(
            50,
            HttpUtility.ParseQueryString(server.Requests[1].Query)["ids"]!
                .Split(',').Length);

        // The fake knows only even users: odd ones are absent (unknown).
        Assert.Equal(75, emails.Count);
        Assert.Equal("user2@example.com", emails[2]);
        Assert.False(emails.ContainsKey(3));
    }

    [Fact]
    public async Task OrganizationNames_AreRead()
    {
        var server = new FakeZendesk(total: 0);

        var names =
            await Directory(server).GetOrganizationNamesAsync([10, 11]);

        Assert.Equal("Example Org 10", names[10]);
        Assert.Equal("Example Org 11", names[11]);
    }

    private static ZendeskSearchCriteria Criteria() =>
        new(FreeText: "example");

    private static ZendeskTicketSearchProvider Provider(
        FakeZendesk server) =>
        new(Client(server));

    private static ZendeskDirectoryProvider Directory(
        FakeZendesk server) =>
        new(Client(server));

    private static ZendeskDeletionApiClient Client(
        FakeZendesk server) =>
        new(
            new HttpClient(server)
            {
                BaseAddress = new Uri("https://example.zendesk.com/")
            },
            ZendeskDeletionOptions.Create("example", "example-public-client"));

    /// <summary>
    /// A scripted Zendesk: search pages, user and organization lookups.
    /// </summary>
    private sealed class FakeZendesk : HttpMessageHandler
    {
        private readonly int _total;
        private readonly bool _reportCount;
        private readonly long[][]? _pages;
        private readonly string[]? _rawPages;
        private readonly HttpStatusCode? _failWith;
        private readonly int _failOnPage;
        private readonly string? _userJson;

        public FakeZendesk(
            int total,
            long[][]? pages = null,
            string[]? rawPages = null,
            bool reportCount = true,
            HttpStatusCode? failWith = null,
            int failOnPage = 1,
            string? userJson = null)
        {
            _total = total;
            _pages = pages;
            _rawPages = rawPages;
            _reportCount = reportCount;
            _failWith = failWith;
            _failOnPage = failOnPage;
            _userJson = userJson;
        }

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);

            var query = HttpUtility.ParseQueryString(uri.Query);

            switch (uri.AbsolutePath)
            {
                case "/api/v2/search.json":
                    return Task.FromResult(Search(int.Parse(query["page"]!)));

                case "/api/v2/users/me.json":
                    return Task.FromResult(
                        Json(HttpStatusCode.OK, _userJson ?? "{}"));

                case "/api/v2/users/show_many.json":
                    return Task.FromResult(Json(
                        HttpStatusCode.OK,
                        JsonSerializer.Serialize(new
                        {
                            users = query["ids"]!.Split(',')
                                .Select(long.Parse)
                                .Where(id => id % 2 == 0)
                                .Select(id => new
                                {
                                    id,
                                    email = $"user{id}@example.com"
                                })
                        })));

                case "/api/v2/organizations/show_many.json":
                    return Task.FromResult(Json(
                        HttpStatusCode.OK,
                        JsonSerializer.Serialize(new
                        {
                            organizations = query["ids"]!.Split(',')
                                .Select(long.Parse)
                                .Select(id => new
                                {
                                    id,
                                    name = $"Example Org {id}"
                                })
                        })));
            }

            return Task.FromResult(Json(HttpStatusCode.NotFound, "{}"));
        }

        private HttpResponseMessage Search(int page)
        {
            if (_failWith is not null && page >= _failOnPage)
            {
                return Json(_failWith.Value, "secret-body");
            }

            if (_rawPages is not null)
            {
                return Json(HttpStatusCode.OK, _rawPages[page - 1]);
            }

            long[] ids;
            bool hasNext;

            if (_pages is not null)
            {
                ids = page <= _pages.Length ? _pages[page - 1] : [];
                hasNext = page < _pages.Length;
            }
            else
            {
                var skip = (page - 1) * 100;

                ids =
                    Enumerable.Range(1, _total)
                        .Skip(skip)
                        .Take(100)
                        .Select(i => (long)i)
                        .ToArray();

                hasNext = skip + 100 < _total;
            }

            var results =
                ids.Select(
                    id => new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["result_type"] = "ticket",
                        ["status"] = "closed"
                    });

            var body =
                new Dictionary<string, object?>
                {
                    ["results"] = results,
                    ["next_page"] = hasNext
                        ? "https://example.zendesk.com/next"
                        : null
                };

            if (_reportCount)
            {
                body["count"] = _total;
            }

            return Json(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(body));
        }

        private static HttpResponseMessage Json(
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
