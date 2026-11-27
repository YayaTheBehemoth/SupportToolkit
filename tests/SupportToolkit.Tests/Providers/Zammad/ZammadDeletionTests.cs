using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BulkDelete.Models;
using SupportToolkit.Modules.BulkDelete.Production;
using SupportToolkit.Modules.BulkDelete.Services;
using SupportToolkit.Providers.Zammad.Configuration;
using SupportToolkit.Providers.Zammad.Deletion;
using SupportToolkit.Providers.Zammad.Transport;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Providers.Zammad;

/// <summary>
/// Bulk delete on Zammad, against a fake that answers as a lab Zammad
/// 7.3.0 did on November 26, 2026 (lab/zammad/README.md). Every value is
/// synthetic.
/// </summary>
public class ZammadDeletionTests : IDisposable
{
    private const string Token = "EXAMPLE0DELETION0000000000000000";

    private static readonly DateTimeOffset Now =
        new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly string _receipts =
        Directory.CreateTempSubdirectory("st-zammad-receipts-").FullName;

    public void Dispose()
    {
        Directory.Delete(_receipts, recursive: true);
    }

    // ---- The search query ----

    [Fact]
    public void TheQuery_JoinsFieldTermsWithAnd()
    {
        Assert.Equal(
            "organization.name:\"Tailspin Toys\" AND customer.email:*tailspin.example " +
            "AND customer.fullname:\"Tom Tailor\" AND \"printer\"",
            ZammadSearchQuery.Build(
                new ZammadSearchCriteria(
                    "Tailspin Toys",
                    null,
                    "@Tailspin.Example",
                    "printer",
                    "Tom Tailor")));

        Assert.Equal(
            "customer.email:\"tom@tailspin.example\"",
            ZammadSearchQuery.Build(
                new ZammadSearchCriteria(null, "tom@tailspin.example", null, null, null)));
    }

    [Theory]
    [InlineData("Tail\"spin", null, null)]
    [InlineData("Tail\\spin", null, null)]
    [InlineData(null, "not-an-address", null)]
    [InlineData(null, null, "localhost")]
    [InlineData(null, null, "tail spin.example")]
    [InlineData(null, null, null)]
    public void AnUnsafeOrEmptyTerm_IsRefused(
        string? organization,
        string? email,
        string? domain)
    {
        Assert.Throws<ArgumentException>(
            () => ZammadSearchQuery.Build(
                new ZammadSearchCriteria(organization, email, domain, null, null)));
    }

    // ---- Search ----

    [Fact]
    public async Task ASearch_ReadsEveryPage_AndIsCompleteWhenTheCountsAgree()
    {
        var zammad = new FakeZammad(Sample(150));

        var result =
            await Provider(zammad).SearchAsync(
                new ZammadSearchCriteria("Tailspin Toys", null, null, null, null),
                CancellationToken.None);

        Assert.True(result.IsComplete, result.IncompleteReason);
        Assert.Equal(150, result.Tickets.Count);
        Assert.Equal(2, zammad.SearchPages);
        Assert.Equal("44001", result.Tickets[0].Number);
        Assert.Equal("closed", result.Tickets[0].State);
        Assert.Equal("Tailspin Toys", result.OrganizationNames[20]);
    }

    [Fact]
    public async Task ASearch_ThatReturnsFewerThanZammadCounted_IsIncomplete()
    {
        var zammad = new FakeZammad(Sample(5)) { ReportedTotal = 7 };

        var result =
            await Provider(zammad).SearchAsync(
                new ZammadSearchCriteria("Tailspin Toys", null, null, null, null),
                CancellationToken.None);

        Assert.False(result.IsComplete);
        Assert.Contains("reported 7", result.IncompleteReason);
    }

    [Fact]
    public async Task LooseMatches_AreDropped_SoOnlyExactlyMatchingTicketsRemain()
    {
        var tickets =
            new[]
            {
                Ticket(1, "closed", organization: 20, customer: 30),
                Ticket(2, "closed", organization: 21, customer: 30),
                Ticket(3, "closed", organization: 20, customer: 31)
            };

        var zammad = new FakeZammad(tickets);

        var byOrganization =
            await Provider(zammad).SearchAsync(
                new ZammadSearchCriteria("tailspin toys", null, null, null, null),
                CancellationToken.None);

        // "Tailspin Toys Holding" also matches Zammad's phrase search.
        Assert.Equal([1L, 3L], byOrganization.Tickets.Select(ticket => ticket.Id));

        var byDomain =
            await Provider(zammad).SearchAsync(
                new ZammadSearchCriteria(null, null, "tailspin.example", null, null),
                CancellationToken.None);

        // "nottailspin.example" also ends in "tailspin.example".
        Assert.Equal([1L, 2L], byDomain.Tickets.Select(ticket => ticket.Id));
    }

    // ---- Sign-in ----

    [Fact]
    public async Task SigningIn_ReadsTheTokensUser_AndARefusedTokenNeedsANewSignIn()
    {
        var zammad = new FakeZammad([]);
        var directory = new ZammadTicketDirectory(Provider(zammad));

        await directory.SignInAsync(CancellationToken.None);

        var user = await directory.GetCurrentUserAsync(CancellationToken.None);

        Assert.Equal("Lab Admin", user.Name);
        Assert.Equal("Admin, Agent", user.Role);

        zammad.RefuseToken = true;

        await Assert.ThrowsAsync<SignInRequiredException>(
            () => directory.SignInAsync(CancellationToken.None));
    }

    // ---- Deletion ----

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task ADeletion_IsFinishedAtOnce_WithItsTicketsResult(
        HttpStatusCode status,
        bool deleted)
    {
        var zammad = new FakeZammad([]) { DeleteStatus = _ => status };

        var job =
            await new ZammadTicketDeleter(Provider(zammad))
                .StartDeleteAsync([7], CancellationToken.None);

        Assert.Equal(DeleteJobState.Completed, job.State);
        Assert.Equal(deleted, Assert.Single(job.Results).Success);
        Assert.Equal(["DELETE /api/v1/tickets/7"], zammad.Deletes);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, DeletionRequestFailure.NotSignedIn)]
    [InlineData(HttpStatusCode.Forbidden, DeletionRequestFailure.Rejected)]
    [InlineData(HttpStatusCode.UnprocessableEntity, DeletionRequestFailure.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, DeletionRequestFailure.Unclear)]
    [InlineData(HttpStatusCode.InternalServerError, DeletionRequestFailure.Unclear)]
    [InlineData(HttpStatusCode.BadGateway, DeletionRequestFailure.Unclear)]
    public async Task AnyOtherAnswer_IsClassified_NeverAssumedDeleted(
        HttpStatusCode status,
        DeletionRequestFailure expected)
    {
        var zammad = new FakeZammad([]) { DeleteStatus = _ => status };

        var error =
            await Assert.ThrowsAsync<DeletionRequestException>(
                () => new ZammadTicketDeleter(Provider(zammad))
                    .StartDeleteAsync([7], CancellationToken.None));

        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain(Token, error.Message);
    }

    [Fact]
    public async Task ATimeout_LeavesTheOutcomeUnclear()
    {
        var zammad = new FakeZammad([]) { DeleteThrows = true };

        var error =
            await Assert.ThrowsAsync<DeletionRequestException>(
                () => new ZammadTicketDeleter(Provider(zammad))
                    .StartDeleteAsync([7], CancellationToken.None));

        Assert.Equal(DeletionRequestFailure.Unclear, error.Failure);
    }

    [Fact]
    public async Task MoreThanOneTicketPerRequest_IsRefused()
    {
        var zammad = new FakeZammad([]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => new ZammadTicketDeleter(Provider(zammad))
                .StartDeleteAsync([7, 8], CancellationToken.None));

        Assert.Empty(zammad.Deletes);
    }

    // ---- The whole flow ----

    [Fact]
    public async Task ThePreview_ShowsZammadsNumbers_AndTheDeletionUsesTheIds()
    {
        var zammad =
            new FakeZammad(
                [
                    Ticket(11, "closed"),
                    Ticket(12, "closed"),
                    Ticket(13, "open")
                ]);

        var workflow =
            new BulkDeleteWorkflow(
                new ZammadTicketDirectory(Provider(zammad)),
                new Clock());

        var operatorUser = await workflow.SignInAsync();

        var preview =
            await workflow.PreviewAsync(
                operatorUser,
                new TicketSearchCriteria(Organization: "Tailspin Toys"));

        Assert.True(preview.Preview.IsSearchComplete);
        Assert.Equal(["#44011", "#44012", "#44013"], preview.Preview.Tickets.Select(ticket => ticket.Shown));

        // Closed tickets of any age are selected; there is no waiting time.
        Assert.Equal(2, preview.Preview.SelectedCount);
        Assert.Equal("Tailspin Toys", preview.Preview.ByOrganization.Single().Name);
        Assert.Equal("tailspin.example", preview.Preview.ByRequesterDomain.Single().Name);

        var plan =
            DeletionPlan.Create(
                preview,
                new DeletionSelection(preview.Preview),
                "127.0.0.1:8100",
                Now);

        Assert.Equal(2, plan.Batches.Count);
        Assert.Equal("#44012", plan.Show(12));

        var run =
            await new DeletionExecutor(
                    new ZammadTicketDeleter(Provider(zammad)),
                    new DeletionReceiptStore(_receipts),
                    new Clock(),
                    (_, _) => Task.CompletedTask)
                .ExecuteAsync(plan, new DeletionConfirmation(2, null));

        Assert.True(run.Outcome.IsComplete);
        Assert.Equal(
            ["DELETE /api/v1/tickets/11", "DELETE /api/v1/tickets/12"],
            zammad.Deletes);

        var receipt = await File.ReadAllTextAsync(run.ReceiptPath);

        Assert.StartsWith("zammad-delete-", Path.GetFileName(run.ReceiptPath));
        Assert.Contains("\"number\": \"44011\"", receipt);
        Assert.DoesNotContain("Synthetic subject", receipt);
    }

    // ---- Configuration ----

    [Fact]
    public void TheDeletionToken_IsNeededOnlyWhenBulkDeleteIsOn()
    {
        var configuration = LabConfiguration();

        Assert.Equal(
            ["API token", "deletion token"],
            ZammadConnections.Instance.GetSecrets(configuration, "lab").Select(secret => secret.Label));

        TurnOffBulkDelete(configuration);

        Assert.Equal(
            ["API token"],
            ZammadConnections.Instance.GetSecrets(configuration, "lab").Select(secret => secret.Label));
    }

    [Fact]
    public async Task WithoutBulkDelete_TheProfileHasNoBulkDelete()
    {
        var configuration = LabConfiguration();
        TurnOffBulkDelete(configuration);

        var (_, _, resolver) = Create(configuration, Secrets("api", "deletion"));

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(resolver, CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Equal("Zammad", availability.SystemName);
        Assert.Contains("not set up for bulk delete", availability.Reason);
    }

    [Fact]
    public async Task TheTokenThatCreatesTickets_IsNeverUsedToDelete()
    {
        var (_, _, resolver) = Create(LabConfiguration(), Secrets("same", "same"));

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(resolver, CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("same token", availability.Reason);
    }

    [Fact]
    public async Task AMissingDeletionToken_IsReported()
    {
        var (_, _, resolver) = Create(LabConfiguration(), Secrets("api", null));

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(resolver, CancellationToken.None);

        Assert.False(availability.IsAvailable);
        Assert.Contains("deletion token", availability.Reason);
    }

    // ---- Support ----

    private static SupportToolkitConfiguration LabConfiguration()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);
        configuration.ActiveProfile = LabProfileComposition.ProfileName;

        return configuration;
    }

    private static void TurnOffBulkDelete(SupportToolkitConfiguration configuration)
    {
        var connections = new Dictionary<string, ZammadConnectionConfiguration>(ZammadConnections.Read(configuration));
        connections["lab"].BulkDelete = false;
        ZammadConnections.Write(configuration, connections);
    }

    private static MemorySecrets Secrets(string? api, string? deletion)
    {
        var values = new Dictionary<string, string>();

        if (api is not null)
        {
            values[ZammadConnections.ApiToken("lab").Key] = api;
        }

        if (deletion is not null)
        {
            values[ZammadConnections.DeletionToken("lab").Key] = deletion;
        }

        return new MemorySecrets(values);
    }

    private static ZammadDeletionProvider Provider(FakeZammad zammad) =>
        new(
            new ZammadApiClient(
                new HttpClient(zammad),
                ZammadOptions.Create("http://127.0.0.1:8100/", Token)));

    private static FakeTicket Ticket(
        long id,
        string state,
        long organization = 20,
        long customer = 30) =>
        new(id, state, organization, customer);

    private static FakeTicket[] Sample(int count) =>
        Enumerable.Range(1, count)
            .Select(id => Ticket(id, id % 10 == 0 ? "open" : "closed"))
            .ToArray();

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record FakeTicket(
        long Id,
        string State,
        long Organization,
        long Customer);

    /// <summary>
    /// A scripted Zammad: users/me, roles, ticket_states, a paged
    /// tickets/search with assets, and DELETE tickets/{id}.
    /// </summary>
    private sealed class FakeZammad : HttpMessageHandler
    {
        private static readonly Dictionary<string, long> States =
            new() { ["new"] = 1, ["open"] = 2, ["closed"] = 4 };

        private readonly FakeTicket[] _tickets;

        public FakeZammad(FakeTicket[] tickets)
        {
            _tickets = tickets;
        }

        public bool RefuseToken { get; set; }

        public int? ReportedTotal { get; init; }

        public Func<long, HttpStatusCode> DeleteStatus { get; init; } = _ => HttpStatusCode.OK;

        public bool DeleteThrows { get; init; }

        public int SearchPages { get; private set; }

        public List<string> Deletes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("Token token=" + Token, request.Headers.Authorization?.ToString());

            if (RefuseToken)
            {
                return Json(HttpStatusCode.Unauthorized, new { error = "Can't find User for Token" });
            }

            var path = request.RequestUri!.AbsolutePath;

            if (request.Method == HttpMethod.Delete)
            {
                Deletes.Add("DELETE " + path);

                if (DeleteThrows)
                {
                    throw new TaskCanceledException("timed out");
                }

                var id = long.Parse(path.Split('/').Last(), CultureInfo.InvariantCulture);
                var status = DeleteStatus(id);

                return status == HttpStatusCode.OK
                    ? Json(status, new { })
                    : Json(status, new { error = "refused", error_human = "Refused by the fake" });
            }

            return path switch
            {
                "/api/v1/users/me" =>
                    Json(HttpStatusCode.OK, new { id = 3, firstname = "Lab", lastname = "Admin", email = "labadmin@example.com", role_ids = new[] { 1, 2 } }),
                "/api/v1/roles" =>
                    Json(HttpStatusCode.OK, new[] { new { id = 1, name = "Admin" }, new { id = 2, name = "Agent" }, new { id = 3, name = "Customer" } }),
                "/api/v1/ticket_states" =>
                    Json(HttpStatusCode.OK, States.Select(pair => new { id = pair.Value, name = pair.Key })),
                "/api/v1/tickets/search" =>
                    Search(HttpUtility.ParseQueryString(request.RequestUri.Query)),
                _ => Json(HttpStatusCode.NotFound, new { error = "not found" })
            };
        }

        private Task<HttpResponseMessage> Search(System.Collections.Specialized.NameValueCollection query)
        {
            SearchPages++;

            var page = int.Parse(query["page"]!, CultureInfo.InvariantCulture);
            var perPage = int.Parse(query["per_page"]!, CultureInfo.InvariantCulture);

            var slice = _tickets.Skip((page - 1) * perPage).Take(perPage).ToArray();

            return Json(
                HttpStatusCode.OK,
                new
                {
                    record_ids = slice.Select(ticket => ticket.Id),
                    total_count = ReportedTotal ?? _tickets.Length,
                    assets = new
                    {
                        Ticket = slice.ToDictionary(
                            ticket => ticket.Id.ToString(CultureInfo.InvariantCulture),
                            ticket => (object)new
                            {
                                id = ticket.Id,
                                number = (44000 + ticket.Id).ToString(CultureInfo.InvariantCulture),
                                title = "Synthetic subject " + ticket.Id,
                                state_id = States[ticket.State],
                                organization_id = ticket.Organization,
                                customer_id = ticket.Customer,
                                created_at = "2026-11-20T10:00:00Z",
                                updated_at = "2026-11-25T10:00:00Z"
                            }),
                        User = new Dictionary<string, object>
                        {
                            ["30"] = new { id = 30, firstname = "Tom", lastname = "Tailor", email = "tom@tailspin.example" },
                            ["31"] = new { id = 31, firstname = "Nora", lastname = "Other", email = "nora@nottailspin.example" }
                        },
                        Organization = new Dictionary<string, object>
                        {
                            ["20"] = new { id = 20, name = "Tailspin Toys" },
                            ["21"] = new { id = 21, name = "Tailspin Toys Holding" }
                        }
                    }
                });
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, object body) =>
            Task.FromResult(
                new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json")
                });
    }
}
