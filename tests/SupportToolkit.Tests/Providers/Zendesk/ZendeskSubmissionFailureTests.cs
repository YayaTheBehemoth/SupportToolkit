
using System.Net;
using System.Text;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskSubmissionFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TicketHttpFailure_IdentifiesZendeskAndAdvisesReconciliation(
        HttpStatusCode status)
    {
        using var client = CreateClient(
            ticketResponse: new HttpResponseMessage(status));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider(client).CreateTicketAsync(
                    Draft(),
                    "fixture-submission-failure"));

        Assert.Contains("Zendesk", exception.Message);
        Assert.Contains(
            ((int)status).ToString(),
            exception.Message);
        Assert.Contains(
            "before retrying",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Acronis",
            exception.Message);
    }

    [Fact]
    public async Task AuthenticationFailure_IdentifiesZendesk()
    {
        using var client =
            new HttpClient(
                new StubHandler(request =>
                {
                    Assert.Equal(
                        "/oauth/tokens",
                        request.RequestUri?.AbsolutePath);

                    return Task.FromResult(
                        new HttpResponseMessage(
                            HttpStatusCode.Unauthorized));
                }));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider(client).CreateTicketAsync(
                    Draft(),
                    "fixture-auth-failure"));

        Assert.Contains("Zendesk", exception.Message);
        Assert.Contains("401", exception.Message);
        Assert.Contains(
            "before retrying",
            exception.Message);
    }

    [Fact]
    public async Task CreatedResponseWithoutTicketId_IsNotReportedAsSuccess()
    {
        using var client = CreateClient(
            ticketResponse: JsonResponse(
                HttpStatusCode.Created,
                """
                {
                  "ticket": {
                    "subject": "Synthetic review"
                  }
                }
                """));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider(client).CreateTicketAsync(
                    Draft(),
                    "fixture-missing-ticket-id"));

        Assert.Contains("HTTP 201", exception.Message);
        Assert.Contains(
            "valid ticket ID",
            exception.Message);
        Assert.Contains(
            "before retrying",
            exception.Message);
    }

    [Fact]
    public async Task MalformedCreatedResponse_IsNotReportedAsSuccess()
    {
        using var client = CreateClient(
            ticketResponse: JsonResponse(
                HttpStatusCode.Created,
                "{ malformed json"));

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateProvider(client).CreateTicketAsync(
                    Draft(),
                    "fixture-malformed-ticket"));

        Assert.Contains("HTTP 201", exception.Message);
        Assert.Contains(
            "before retrying",
            exception.Message);
    }

    private static ZendeskTicketProvider CreateProvider(
        HttpClient httpClient)
    {
        var apiClient =
            new ZendeskApiClient(
                httpClient,
                new ZendeskOptions
                {
                    Subdomain = "supporttoolkit-test",
                    ClientId = "fixture-client",
                    ClientSecret = "fixture-secret"
                });

        return new ZendeskTicketProvider(
            apiClient,
            groupId: 12345);
    }

    private static TicketDraft Draft()
    {
        return new TicketDraft
        {
            Subject = "Synthetic review",
            Body = "Synthetic test content."
        };
    }

    private static HttpClient CreateClient(
        HttpResponseMessage ticketResponse)
    {
        return new HttpClient(
            new StubHandler(request =>
            {
                var path =
                    request.RequestUri?.AbsolutePath;

                if (path == "/oauth/tokens")
                {
                    return Task.FromResult(
                        JsonResponse(
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
                    path);

                Assert.True(
                    request.Headers.Contains("Idempotency-Key"));

                return Task.FromResult(ticketResponse);
            }));
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode status,
        string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            Task<HttpResponseMessage>> _handle;

        public StubHandler(
            Func<
                HttpRequestMessage,
                Task<HttpResponseMessage>> handle)
        {
            _handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handle(request);
        }
    }
}
