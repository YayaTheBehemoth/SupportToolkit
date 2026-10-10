using System.Net;
using System.Text;
using System.Text.Json;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskTicketProviderTests
{
    [Fact]
    public async Task CreateTicketAsync_AuthenticatesAndCreatesPrivateTicket()
    {
        var tokenRequests =
            0;

        var ticketRequests =
            0;

        var handler =
            new StubHttpMessageHandler(
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
                            JsonDocument.Parse(
                                body
                            );

                        Assert.Equal(
                            "client_credentials",
                            json.RootElement
                                .GetProperty(
                                    "grant_type"
                                )
                                .GetString()
                        );

                        Assert.Equal(
                            "supportToolkit",
                            json.RootElement
                                .GetProperty(
                                    "client_id"
                                )
                                .GetString()
                        );

                        Assert.Equal(
                            "fixture-secret",
                            json.RootElement
                                .GetProperty(
                                    "client_secret"
                                )
                                .GetString()
                        );

                        Assert.Equal(
                            "tickets:write",
                            json.RootElement
                                .GetProperty(
                                    "scope"
                                )
                                .GetString()
                        );

                        return JsonResponse(
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
                            Assert.Single(
                                idempotencyValues!
                            )
                        );

                        var body =
                            await request.Content!
                                .ReadAsStringAsync();

                        using var json =
                            JsonDocument.Parse(
                                body
                            );

                        var ticket =
                            json.RootElement
                                .GetProperty(
                                    "ticket"
                                );

                        Assert.Equal(
                            "SupportToolkit test",
                            ticket
                                .GetProperty(
                                    "subject"
                                )
                                .GetString()
                        );

                        var comment =
                            ticket.GetProperty(
                                "comment"
                            );

                        Assert.Equal(
                            "Synthetic Zendesk provider test.",
                            comment
                                .GetProperty(
                                    "body"
                                )
                                .GetString()
                        );

                        Assert.False(
                            comment
                                .GetProperty(
                                    "public"
                                )
                                .GetBoolean()
                        );

                        var response =
                            JsonResponse(
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

        using var httpClient =
            new HttpClient(
                handler
            );

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                new ZendeskOptions
                {
                    Subdomain =
                        "supporttoolkit-test",

                    ClientId =
                        "supportToolkit",

                    ClientSecret =
                        "fixture-secret"
                }
            );

        var provider =
            new ZendeskTicketProvider(
                apiClient
            );

        var first =
            await provider.CreateTicketAsync(
                new TicketDraft
                {
                    Subject =
                        "SupportToolkit test",

                    Body =
                        "Synthetic Zendesk provider test."
                },
                "fixture-ticket-001"
            );

        var second =
            await provider.CreateTicketAsync(
                new TicketDraft
                {
                    Subject =
                        "SupportToolkit test",

                    Body =
                        "Synthetic Zendesk provider test."
                },
                "fixture-ticket-002"
            );

        Assert.Equal(
            "123",
            first.Id
        );

        Assert.Equal(
            "123",
            second.Id
        );

        Assert.Equal(
            1,
            tokenRequests
        );

        Assert.Equal(
            2,
            ticketRequests
        );
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode
        )
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                )
        };
    }

    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            Task<HttpResponseMessage>>
            _handler;

        public StubHttpMessageHandler(
            Func<
                HttpRequestMessage,
                Task<HttpResponseMessage>>
                handler)
        {
            _handler =
                handler;
        }

        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            return _handler(
                request
            );
        }
    }
}
