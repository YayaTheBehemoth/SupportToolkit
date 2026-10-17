
using System.Net;
using System.Text.Json;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskTicketProviderTests
{
    [Fact]
    public async Task CreateTicketAsync_AuthenticatesAndCreatesPrivateTicketInSelectedGroup()
    {
        const long groupId = 91000000000000L;

        var tokenRequests = 0;
        var ticketRequests = 0;

        using var httpClient = ZendeskTestHttpSupport.CreateClient(
                async request =>
                {
                    if (request.RequestUri?.AbsolutePath
                        == "/oauth/tokens")
                    {
                        tokenRequests++;

                        Assert.Equal(
                            HttpMethod.Post,
                            request.Method
                        );

                        var body =
                            await request.Content!
                                .ReadAsStringAsync();

                        using var json =
                            JsonDocument.Parse(body);

                        Assert.Equal(
                            "client_credentials",
                            json.RootElement
                                .GetProperty("grant_type")
                                .GetString()
                        );

                        Assert.Equal(
                            "supportToolkit",
                            json.RootElement
                                .GetProperty("client_id")
                                .GetString()
                        );

                        Assert.Equal(
                            "fixture-secret",
                            json.RootElement
                                .GetProperty("client_secret")
                                .GetString()
                        );

                        Assert.Equal(
                            "tickets:write",
                            json.RootElement
                                .GetProperty("scope")
                                .GetString()
                        );

                        return ZendeskTestHttpSupport.JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "access_token": "fixture-access-token",
                              "token_type": "bearer",
                              "expires_in": 3600,
                              "scope": "tickets:write"
                            }
                            """
                        );
                    }

                    if (request.RequestUri?.AbsolutePath
                        == "/api/v2/tickets.json")
                    {
                        ticketRequests++;

                        Assert.Equal(
                            HttpMethod.Post,
                            request.Method
                        );

                        Assert.Equal(
                            "Bearer",
                            request.Headers.Authorization?.Scheme
                        );

                        Assert.Equal(
                            "fixture-access-token",
                            request.Headers.Authorization?.Parameter
                        );

                        Assert.True(
                            request.Headers.TryGetValues(
                                "Idempotency-Key",
                                out var idempotencyValues
                            )
                        );

                        Assert.Equal(
                            ticketRequests == 1
                                ? "fixture-ticket-001"
                                : "fixture-ticket-002",
                            Assert.Single(idempotencyValues!)
                        );

                        var body =
                            await request.Content!
                                .ReadAsStringAsync();

                        using var json =
                            JsonDocument.Parse(body);

                        var ticket =
                            json.RootElement
                                .GetProperty("ticket");

                        Assert.Equal(
                            "SupportToolkit test",
                            ticket
                                .GetProperty("subject")
                                .GetString()
                        );

                        Assert.Equal(
                            groupId,
                            ticket
                                .GetProperty("group_id")
                                .GetInt64()
                        );

                        var comment =
                            ticket.GetProperty("comment");

                        Assert.Equal(
                            "Synthetic Zendesk provider test.",
                            comment
                                .GetProperty("body")
                                .GetString()
                        );

                        Assert.False(
                            comment
                                .GetProperty("public")
                                .GetBoolean()
                        );

                        var response =
                            ZendeskTestHttpSupport.JsonResponse(
                                HttpStatusCode.Created,
                                """
                                {
                                  "ticket": {
                                    "id": 123,
                                    "subject": "SupportToolkit test",
                                    "url": "https://supporttoolkit-test.zendesk.com/api/v2/tickets/123.json"
                                  }
                                }
                                """
                            );

                        response.Headers.Add(
                            "x-idempotency-lookup",
                            "miss"
                        );

                        return response;
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                CreateOptions()
            );

        var provider =
            new ZendeskTicketProvider(
                apiClient,
                groupId
            );

        var draft =
            new TicketDraft
            {
                Subject =
                    "SupportToolkit test",

                Body =
                    "Synthetic Zendesk provider test."
            };

        var first =
            await provider.CreateTicketAsync(
                draft,
                "fixture-ticket-001"
            );

        var second =
            await provider.CreateTicketAsync(
                draft,
                "fixture-ticket-002"
            );

        Assert.Equal("123", first.Id);
        Assert.Equal("123", second.Id);

        Assert.Equal(1, tokenRequests);
        Assert.Equal(2, ticketRequests);
    }

    [Theory]
    [InlineData(111111L)]
    [InlineData(222222L)]
    public async Task CreateTicketAsync_UsesSuppliedGroupId(
        long groupId)
    {
        var ticketRequests = 0;

        using var httpClient = ZendeskTestHttpSupport.CreateClient(
                async request =>
                {
                    if (request.RequestUri?.AbsolutePath
                        == "/oauth/tokens")
                    {
                        return ZendeskTestHttpSupport.JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "access_token": "fixture-access-token",
                              "token_type": "bearer",
                              "expires_in": 3600,
                              "scope": "tickets:write"
                            }
                            """
                        );
                    }

                    if (request.RequestUri?.AbsolutePath
                        == "/api/v2/tickets.json")
                    {
                        ticketRequests++;

                        var body =
                            await request.Content!
                                .ReadAsStringAsync();

                        using var json =
                            JsonDocument.Parse(body);

                        Assert.Equal(
                            groupId,
                            json.RootElement
                                .GetProperty("ticket")
                                .GetProperty("group_id")
                                .GetInt64()
                        );

                        return ZendeskTestHttpSupport.JsonResponse(
                            HttpStatusCode.Created,
                            """
                            {
                              "ticket": {
                                "id": 123,
                                "subject": "Routing test"
                              }
                            }
                            """
                        );
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

        var provider =
            new ZendeskTicketProvider(
                new ZendeskApiClient(
                    httpClient,
                    CreateOptions()
                ),
                groupId
            );

        var result =
            await provider.CreateTicketAsync(
                new TicketDraft
                {
                    Subject = "Routing test",
                    Body = "Synthetic test body."
                },
                "fixture-routing-test"
            );

        Assert.Equal("123", result.Id);
        Assert.Equal(1, ticketRequests);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Constructor_InvalidGroupId_ThrowsBeforeAnyHttpRequest(
        long groupId)
    {
        var requestCount = 0;

        using var httpClient = ZendeskTestHttpSupport.CreateClient(
                request =>
                {
                    requestCount++;

                    return Task.FromResult(
                        new HttpResponseMessage(
                            HttpStatusCode.InternalServerError
                        )
                    );
                }
            );

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                CreateOptions()
            );

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ZendeskTicketProvider(
                apiClient,
                groupId
            )
        );

        Assert.Equal(0, requestCount);
    }

    private static ZendeskOptions CreateOptions()
    {
        return new ZendeskOptions
        {
            Subdomain = "supporttoolkit-test",
            ClientId = "supportToolkit",
            ClientSecret = "fixture-secret"
        };
    }

}
