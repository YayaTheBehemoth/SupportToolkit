using System.Net;
using System.Text;
using System.Text.Json;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.Zammad.Tickets;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Tests.Providers.Zammad;

/// <summary>
/// The Zammad ticket API as recorded from a lab Zammad 7.3.0 on
/// November 25, 2026 (lab/zammad/README.md). Every value is synthetic.
/// </summary>
public class ZammadTicketProviderTests
{
    private const string Token = "EXAMPLE0TOKEN00000000000000000A";

    private static readonly TicketDraft Draft =
        new()
        {
            Subject = "Backup review: 2 findings",
            Body = "Tenant A, Tenant B: 3 failed — 50% done\n<b>not html</b>"
        };

    // Recorded responses (JSON), shortened to the fields that matter.
    private const string Created =
        """{"id":11,"group_id":1,"priority_id":2,"state_id":1,"organization_id":null,"number":"65011","title":"Backup review: 2 findings","customer_id":8,"article_ids":[10]}""";
    private const string TokenRefused =
        """{"error":"Can't find User for Token","error_human":"Can't find User for Token"}""";
    private const string Malformed =
        """{"error":"Error occurred while parsing request parameters","error_human":"Error occurred while parsing request parameters"}""";
    private const string UnknownGroup =
        """{"error":"No lookup value found for 'group': \"NoSuchGroup\"","error_human":"No lookup value found for 'group': \"NoSuchGroup\"","unprocessable_content":null}""";
    private const string InvalidCustomer =
        """{"error":"Invalid email 'not-an-email' of customer"}""";
    private const string NotAuthorized =
        """{"error":"Not authorized","error_human":"Not authorized"}""";

    [Fact]
    public async Task ACreatedTicket_IsTheNumberZammadReturns_WithAnAgentLink()
    {
        var (provider, requests) =
            Provider(_ => Json(HttpStatusCode.Created, Created));

        var ticket =
            await provider.CreateTicketAsync(Draft, "ignored-key");

        Assert.Equal("65011", ticket.Id);
        Assert.Equal(Draft.Subject, ticket.Subject);
        Assert.False(ticket.WasIdempotentReplay);
        Assert.Equal(
            "https://helpdesk.example.com/#ticket/zoom/11",
            ticket.ViewUrl);
        Assert.Single(requests);
    }

    [Fact]
    public async Task TheRequest_CarriesTheToken_TheGroup_AndAPlainTextNote()
    {
        var (provider, requests) =
            Provider(_ => Json(HttpStatusCode.Created, Created));

        await provider.CreateTicketAsync(Draft, "ignored-key");

        var request = Assert.Single(requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://helpdesk.example.com/api/v1/tickets", request.Uri);
        Assert.Equal("Token token=" + Token, request.Authorization);
        Assert.False(request.HasIdempotencyHeader);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;

        Assert.Equal(Draft.Subject, root.GetProperty("title").GetString());
        Assert.Equal("Users", root.GetProperty("group").GetString());
        Assert.Equal("guess:backups@example.com", root.GetProperty("customer_id").GetString());

        var article = root.GetProperty("article");

        Assert.Equal(Draft.Body, article.GetProperty("body").GetString());
        Assert.Equal("text/plain", article.GetProperty("content_type").GetString());
        Assert.Equal("note", article.GetProperty("type").GetString());
        Assert.False(article.GetProperty("internal").GetBoolean());
    }

    [Fact]
    public async Task AnInstanceInAFolder_IsAddressedBelowThatFolder()
    {
        var (provider, requests) =
            Provider(
                _ => Json(HttpStatusCode.Created, Created),
                baseUrl: "https://www.example.com/zammad");

        var ticket = await provider.CreateTicketAsync(Draft, "ignored-key");

        Assert.Equal(
            "https://www.example.com/zammad/api/v1/tickets",
            Assert.Single(requests).Uri);
        Assert.Equal(
            "https://www.example.com/zammad/#ticket/zoom/11",
            ticket.ViewUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, TokenRefused, "refused the API token")]
    [InlineData(HttpStatusCode.Forbidden, NotAuthorized, "Not authorized")]
    [InlineData(HttpStatusCode.BadRequest, Malformed, "Error occurred while parsing request parameters")]
    [InlineData(HttpStatusCode.UnprocessableEntity, UnknownGroup, "No lookup value found for 'group'")]
    [InlineData(HttpStatusCode.UnprocessableEntity, InvalidCustomer, "Invalid email")]
    public async Task ARefusalBeforeCreation_SaysNoTicketWasCreated(
        HttpStatusCode status,
        string body,
        string expected)
    {
        var (provider, requests) = Provider(_ => Json(status, body));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.Contains(expected, error.Message);
        Assert.Contains("No ticket was created.", error.Message);
        Assert.DoesNotContain("not confirmed", error.Message);
        Assert.DoesNotContain(Token, error.Message);
        Assert.Single(requests);
    }

    public static TheoryData<HttpStatusCode, string> UnclearAnswers() =>
        new()
        {
            { HttpStatusCode.InternalServerError, """{"error":"something broke"}""" },
            { HttpStatusCode.BadGateway, "" },
            { HttpStatusCode.OK, Created },
            { HttpStatusCode.Found, "" },
            { HttpStatusCode.Created, "" },
            { HttpStatusCode.Created, """{"id":11}""" },
            { HttpStatusCode.Created, "<html><body>Login</body></html>" }
        };

    [Theory]
    [MemberData(nameof(UnclearAnswers))]
    public async Task AnyOtherAnswer_LeavesTheOutcomeUnknown_AndIsNotRetried(
        HttpStatusCode status,
        string body)
    {
        var (provider, requests) = Provider(_ => Json(status, body));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.Contains("not confirmed", error.Message);
        Assert.Contains("a retry creates another ticket", error.Message);
        Assert.DoesNotContain("No ticket was created", error.Message);
        Assert.Single(requests);
    }

    [Fact]
    public async Task ATimeout_LeavesTheOutcomeUnknown()
    {
        var (provider, requests) =
            Provider(_ => throw new TaskCanceledException("timed out"));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.Contains("timed out", error.Message);
        Assert.Contains("not confirmed", error.Message);
        Assert.Single(requests);
    }

    [Fact]
    public async Task ALongRejection_IsShortenedToOneLine()
    {
        var body =
            JsonSerializer.Serialize(new { error = "bad\n" + new string('a', 1000) });

        var (provider, _) =
            Provider(_ => Json(HttpStatusCode.UnprocessableEntity, body));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.DoesNotContain("\n", error.Message);
        Assert.True(error.Message.Length < 450);
    }

    [Fact]
    public void AMissingGroupOrCustomer_IsRefusedBeforeAnyRequest()
    {
        var client =
            new ZammadApiClient(
                new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException())),
                ZammadOptions.Create("https://helpdesk.example.com/", Token));

        Assert.Throws<ArgumentException>(
            () => new ZammadTicketProvider(client, " ", "backups@example.com"));

        Assert.Throws<ArgumentException>(
            () => new ZammadTicketProvider(client, "Users", "not-an-address"));
    }

    [Theory]
    [InlineData("http://helpdesk.example.com/", Token)]
    [InlineData("https://user:pw@helpdesk.example.com/", Token)]
    [InlineData("https://helpdesk.example.com/?x=1", Token)]
    [InlineData("https://helpdesk.example.com/", "has space")]
    [InlineData("not a url", Token)]
    public void AnUnsafeOrInvalidConnection_IsRefused(string baseUrl, string token)
    {
        Assert.Throws<InvalidOperationException>(
            () => ZammadOptions.Create(baseUrl, token));
    }

    [Fact]
    public void PlainHttp_IsAcceptedForALoopbackLabOnly()
    {
        Assert.Equal(
            "http://127.0.0.1:8100/",
            ZammadOptions.Create("http://127.0.0.1:8100", Token).BaseUri.ToString());
    }

    private static (ZammadTicketProvider Provider, List<RecordedRequest> Requests)
        Provider(
            Func<HttpRequestMessage, HttpResponseMessage> respond,
            string baseUrl = "https://helpdesk.example.com/")
    {
        var handler = new RecordingHandler(respond);

        var provider =
            new ZammadTicketProvider(
                new ZammadApiClient(
                    new HttpClient(handler),
                    ZammadOptions.Create(baseUrl, Token)),
                "Users",
                "backups@example.com");

        return (provider, handler.Requests);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    internal sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? Authorization,
        bool HasIdempotencyHeader,
        string Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(
                new RecordedRequest(
                    request.Method,
                    request.RequestUri!.ToString(),
                    request.Headers.TryGetValues("Authorization", out var values)
                        ? values.Single()
                        : null,
                    request.Headers.Contains("Idempotency-Key"),
                    request.Content is null
                        ? ""
                        : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _respond(request);
        }
    }
}
