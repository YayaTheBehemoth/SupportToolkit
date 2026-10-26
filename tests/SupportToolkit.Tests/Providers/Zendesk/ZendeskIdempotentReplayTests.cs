
using System.Net;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskIdempotentReplayTests
{
    [Theory]
    [InlineData("miss", false)]
    [InlineData("hit", true)]
    [InlineData(null, null)]
    [InlineData("unexpected", null)]
    public async Task CreateTicketAsync_ReportsActualReplayStatus(
        string? lookup,
        bool? expectedReplay)
    {
        var ticketRequests = 0;

        using var httpClient =
            ZendeskTestHttpSupport.CreateClient(request =>
                {
                    if (request.RequestUri?.AbsolutePath
                        == "/oauth/tokens")
                    {
                        return Task.FromResult(
                            ZendeskTestHttpSupport.JsonResponse(
                                HttpStatusCode.OK,
                                """
                                {
                                  "access_token": "fixture-token",
                                  "token_type": "bearer",
                                  "expires_in": 3600
                                }
                                """));
                    }

                    Assert.Equal(
                        "/api/v2/tickets.json",
                        request.RequestUri?.AbsolutePath);

                    ticketRequests++;

                    Assert.True(
                        request.Headers.Contains(
                            "Idempotency-Key"));

                    var response =
                        ZendeskTestHttpSupport.JsonResponse(
                            HttpStatusCode.Created,
                            """
                            {
                              "ticket": {
                                "id": 12345,
                                "subject": "Synthetic backup review",
                                "url": "https://fixture.zendesk.com/api/v2/tickets/12345.json"
                              }
                            }
                            """);

                    if (lookup is not null)
                    {
                        response.Headers.TryAddWithoutValidation(
                            "x-idempotency-lookup",
                            lookup);
                    }

                    return Task.FromResult(response);
                });

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                new ZendeskOptions
                {
                    Subdomain = "fixture",
                    ClientId = "fixture-client",
                    ClientSecret = "fixture-secret"
                });

        var provider =
            new ZendeskTicketProvider(
                apiClient,
                111111,
                ZendeskTestHttpSupport.Identity());

        var ticket =
            await provider.CreateTicketAsync(
                new TicketDraft
                {
                    Subject = "Synthetic backup review",
                    Body = "Synthetic content."
                },
                "fixture-replay-key");

        Assert.Equal(1, ticketRequests);

        Assert.Equal("12345", ticket.Id);

        Assert.Equal(
            expectedReplay,
            ticket.WasIdempotentReplay);

        Assert.Equal(
            "https://fixture.zendesk.com/agent/tickets/12345",
            ticket.ViewUrl);

        Assert.Equal(
            "https://fixture.zendesk.com/api/v2/tickets/12345.json",
            ticket.Url);
    }

    [Fact]
    public void TicketReporter_ReplayShowsExistingTicket()
    {
        var ticket =
            new CreatedTicket(
                "12345",
                "Synthetic backup review",
                null)
            {
                WasIdempotentReplay = true,
                ViewUrl =
                    "https://fixture.zendesk.com/agent/tickets/12345"
            };

        using var output = new StringWriter();

        ConsoleCreatedTicketReporter.Write(
            output,
            ticket);

        var text = output.ToString();

        Assert.Contains(
            "Ticket already submitted",
            text);

        Assert.Contains(
            "No new ticket was created",
            text);

        Assert.Contains("12345", text);

        Assert.Contains(
            "https://fixture.zendesk.com/agent/tickets/12345",
            text);

        Assert.DoesNotContain(
            "New ticket created successfully",
            text);
    }

    [Fact]
    public void TicketReporter_MissShowsNewTicket()
    {
        var ticket =
            new CreatedTicket(
                "12345",
                "Synthetic backup review",
                null)
            {
                WasIdempotentReplay = false
            };

        using var output = new StringWriter();

        ConsoleCreatedTicketReporter.Write(
            output,
            ticket);

        var text = output.ToString();

        Assert.Contains(
            "New ticket created successfully",
            text);

        Assert.DoesNotContain(
            "Ticket already submitted",
            text);
    }

    [Fact]
    public void TicketReporter_MissingHeaderDoesNotGuess()
    {
        var ticket =
            new CreatedTicket(
                "12345",
                "Synthetic backup review",
                null);

        using var output = new StringWriter();

        ConsoleCreatedTicketReporter.Write(
            output,
            ticket);

        var text = output.ToString();

        Assert.Contains(
            "Ticket confirmed",
            text);

        Assert.Contains(
            "did not report",
            text);

        Assert.DoesNotContain(
            "New ticket created successfully",
            text);

        Assert.DoesNotContain(
            "Ticket already submitted",
            text);
    }

}
