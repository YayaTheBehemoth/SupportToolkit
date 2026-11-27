using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using SupportToolkit.Modules.BulkDelete.Models;
using SupportToolkit.Modules.BulkDelete.Production;
using SupportToolkit.Modules.BulkDelete.Services;
using SupportToolkit.Providers.Zammad.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Tests.Modules.BulkDelete;

/// <summary>
/// Searching for one ticket by its number, and the link to each ticket,
/// for every system: the module asks, each provider answers its own way.
/// </summary>
public class TicketNumberAndLinkTests
{
    // ---- Zendesk ----

    [Fact]
    public void Zendesk_SearchesForTheNumber_AsATerm()
    {
        Assert.Equal(
            "type:ticket organization:\"Example Org\" 12345",
            ZendeskSearchQuery.Build(
                ZendeskTicketDirectory.ToZendesk(
                    new TicketSearchCriteria(
                        Organization: "Example Org",
                        TicketNumber: "#12345"))));
    }

    [Theory]
    [InlineData("12a")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("1 2")]
    public void Zendesk_RefusesANumberThatIsNotOne(string number)
    {
        Assert.Throws<ArgumentException>(
            () => ZendeskTicketDirectory.ToZendesk(
                new TicketSearchCriteria(TicketNumber: number)));
    }

    [Fact]
    public async Task Zendesk_KeepsOnlyTheTicketWithExactlyThatNumber()
    {
        // A text search for a number also finds tickets that mention it.
        var provider =
            new ZendeskTicketSearchProvider(
                new ZendeskDeletionApiClient(
                    new HttpClient(new ZendeskSearch([12345, 22, 1234])),
                    ZendeskDeletionOptions.Create("example", "example-public-client")));

        var result =
            await provider.SearchTicketsAsync(
                new ZendeskSearchCriteria(TicketId: 12345));

        Assert.True(result.IsComplete, result.IncompleteReason);
        Assert.Equal([12345L], result.Tickets.Select(ticket => ticket.Id));
    }

    [Fact]
    public void Zendesk_LinksToTheAgentView_OnTheConfiguredInstance()
    {
        Assert.Equal(
            "https://example.zendesk.com/agent/tickets/12345",
            ZendeskTicketDirectory.AgentLink(
                ZendeskDeletionOptions.Create("example", "example-public-client").BaseUri,
                12345).AbsoluteUri);
    }

    // ---- Zammad ----

    [Theory]
    [InlineData("44013")]
    [InlineData("#44013")]
    [InlineData("Ticket#44013")]
    public void Zammad_SearchesTheNumberField(string number)
    {
        Assert.Equal(
            "number:44013",
            ZammadSearchQuery.Build(
                new ZammadSearchCriteria(null, null, null, null, null, number)));
    }

    [Fact]
    public void Zammad_RefusesANumberThatIsNotOne()
    {
        Assert.Throws<ArgumentException>(
            () => ZammadSearchQuery.Build(
                new ZammadSearchCriteria(null, null, null, null, null, "44013 OR *")));
    }

    // ---- The module ----

    [Fact]
    public void ANumberAlone_IsASearchTerm()
    {
        Assert.True(new TicketSearchCriteria(TicketNumber: "44013").HasAny);
    }

    [Fact]
    public void TheLink_ReachesThePreview()
    {
        var link = new Uri("https://helpdesk.example.com/#ticket/zoom/13");

        var preview =
            DeletionPreviewBuilder.Build(
                new TicketSearchResult(
                    [
                        new TicketSummary(13, "closed", "Example", 1, 2, null, null, null, "44013", link)
                    ],
                    1,
                    0,
                    true,
                    null),
                null,
                null,
                null,
                DateTimeOffset.UnixEpoch,
                ZammadDeletionRules.Rules);

        var ticket = Assert.Single(preview.Tickets);

        Assert.Equal(link, ticket.Link);
        Assert.Equal("#44013", ticket.Shown);
    }

    /// <summary>One page of Zendesk search results with the given IDs.</summary>
    private sealed class ZendeskSearch(long[] ids) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query)["query"];

            Assert.Equal("type:ticket 12345", query);

            var body =
                JsonSerializer.Serialize(
                    new
                    {
                        results = ids.Select(id => new
                        {
                            id,
                            result_type = "ticket",
                            status = "closed",
                            subject = "Example " + id,
                            created_at = "2026-11-01T10:00:00Z",
                            updated_at = "2026-11-02T10:00:00Z"
                        }),
                        count = ids.Length,
                        next_page = (string?)null
                    });

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
        }
    }
}
