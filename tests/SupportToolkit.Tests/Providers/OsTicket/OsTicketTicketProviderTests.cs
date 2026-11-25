using System.Net;
using System.Text;
using System.Text.Json;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Providers.OsTicket.Tickets;
using SupportToolkit.Providers.OsTicket.Transport;

namespace SupportToolkit.Tests.Providers.OsTicket;

/// <summary>
/// The osTicket ticket API as recorded from a lab osTicket 1.17.5 on
/// November 20, 2026 (lab/osticket/README.md). Every value is synthetic.
/// </summary>
public class OsTicketTicketProviderTests
{
    private const string ApiKey = "EXAMPLE0API0KEY0000000000000000A";

    private static readonly TicketDraft Draft =
        new()
        {
            Subject = "Backup review: 2 findings",
            Body = "Tenant A, Tenant B: 3 failed — 50% done\n<b>not html</b>"
        };

    // Recorded responses (text bodies, osTicket labels them text/html).
    private const string Created = "655387";
    private const string KeyRefused = "Valid API key required";
    private const string Malformed = "Syntax error, malformed JSON";
    private const string MissingMessage =
        "Unable to create new ticket :21\nIssue Details is a required field";

    [Fact]
    public async Task ACreatedTicket_IsTheNumberOsTicketReturns_WithAStaffLink()
    {
        var (provider, requests) =
            Provider(_ => Text(HttpStatusCode.Created, Created));

        var ticket =
            await provider.CreateTicketAsync(Draft, "ignored-key");

        Assert.Equal("655387", ticket.Id);
        Assert.Equal(Draft.Subject, ticket.Subject);
        Assert.False(ticket.WasIdempotentReplay);
        Assert.Equal(
            "https://helpdesk.example.com/scp/tickets.php?number=655387",
            ticket.ViewUrl);
        Assert.Single(requests);
    }

    [Fact]
    public async Task TheRequest_CarriesTheKey_ThePlainTextBody_AndNoAlerts()
    {
        var (provider, requests) =
            Provider(_ => Text(HttpStatusCode.Created, Created));

        await provider.CreateTicketAsync(Draft, "ignored-key");

        var request = Assert.Single(requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://helpdesk.example.com/api/tickets.json",
            request.Uri);
        Assert.Equal(ApiKey, request.ApiKey);
        Assert.False(request.HasIdempotencyHeader);

        using var json = JsonDocument.Parse(request.Body);
        var root = json.RootElement;

        Assert.Equal("Example Requester", root.GetProperty("name").GetString());
        Assert.Equal("requester@example.com", root.GetProperty("email").GetString());
        Assert.Equal(Draft.Subject, root.GetProperty("subject").GetString());
        Assert.Equal(
            "data:text/plain;charset=utf-8," + Draft.Body,
            root.GetProperty("message").GetString());
        Assert.Equal(10, root.GetProperty("topicId").GetInt32());
        Assert.False(root.GetProperty("alert").GetBoolean());
        Assert.False(root.GetProperty("autorespond").GetBoolean());
        Assert.Equal("API", root.GetProperty("source").GetString());
    }

    [Fact]
    public async Task AHelpdeskInAFolder_IsAddressedBelowThatFolder()
    {
        var (provider, requests) =
            Provider(
                _ => Text(HttpStatusCode.Created, Created),
                baseUrl: "https://www.example.com/support");

        var ticket = await provider.CreateTicketAsync(Draft, "ignored-key");

        Assert.Equal(
            "https://www.example.com/support/api/tickets.json",
            Assert.Single(requests).Uri);
        Assert.Equal(
            "https://www.example.com/support/scp/tickets.php?number=655387",
            ticket.ViewUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, KeyRefused, "source IP address")]
    [InlineData(HttpStatusCode.BadRequest, Malformed, "malformed JSON")]
    [InlineData(HttpStatusCode.InternalServerError, MissingMessage, "Issue Details is a required field")]
    public async Task ARefusalBeforeCreation_SaysNoTicketWasCreated(
        HttpStatusCode status,
        string body,
        string expected)
    {
        var (provider, requests) = Provider(_ => Text(status, body));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.Contains(expected, error.Message);
        Assert.Contains("No ticket was created.", error.Message);
        Assert.DoesNotContain("not confirmed", error.Message);
        Assert.DoesNotContain(ApiKey, error.Message);
        Assert.Single(requests);
    }

    public static TheoryData<HttpStatusCode, string> UnclearAnswers() =>
        new()
        {
            { HttpStatusCode.InternalServerError, "Fatal error in some plugin" },
            { HttpStatusCode.BadGateway, "" },
            { HttpStatusCode.OK, Created },
            { HttpStatusCode.Found, "" },
            { HttpStatusCode.Created, "" },
            { HttpStatusCode.Created, "<html><body>Login</body></html>" }
        };

    [Theory]
    [MemberData(nameof(UnclearAnswers))]
    public async Task AnyOtherAnswer_LeavesTheOutcomeUnknown_AndIsNotRetried(
        HttpStatusCode status,
        string body)
    {
        var (provider, requests) = Provider(_ => Text(status, body));

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
    public async Task ALostConnection_LeavesTheOutcomeUnknown()
    {
        var (provider, _) =
            Provider(_ => throw new HttpRequestException("connection reset"));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.Contains("not confirmed", error.Message);
    }

    [Fact]
    public async Task ALongRejection_IsShortenedToOneLine()
    {
        var body =
            "Unable to create new ticket :x\n" + new string('a', 1000);

        var (provider, _) =
            Provider(_ => Text(HttpStatusCode.InternalServerError, body));

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateTicketAsync(Draft, "ignored-key"));

        Assert.DoesNotContain("\n", error.Message);
        Assert.True(error.Message.Length < 450);
    }

    [Fact]
    public void AnInvalidTopicOrRequester_IsRefusedBeforeAnyRequest()
    {
        var client =
            new OsTicketApiClient(
                new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException())),
                OsTicketOptions.Create("https://helpdesk.example.com/", ApiKey));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OsTicketTicketProvider(client, 0, Requester()));

        Assert.Throws<ArgumentException>(
            () => new OsTicketRequester("Example", "not-an-address"));

        Assert.Throws<ArgumentException>(
            () => new OsTicketRequester(" ", "requester@example.com"));
    }

    private static OsTicketRequester Requester() =>
        new("Example Requester", "requester@example.com");

    private static (OsTicketTicketProvider Provider, List<RecordedRequest> Requests)
        Provider(
            Func<HttpRequestMessage, HttpResponseMessage> respond,
            string baseUrl = "https://helpdesk.example.com/")
    {
        var handler = new RecordingHandler(respond);

        var provider =
            new OsTicketTicketProvider(
                new OsTicketApiClient(
                    new HttpClient(handler),
                    OsTicketOptions.Create(baseUrl, ApiKey)),
                10,
                Requester());

        return (provider, handler.Requests);
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html")
        };

    internal sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? ApiKey,
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
                    request.Headers.TryGetValues("X-API-Key", out var keys)
                        ? keys.Single()
                        : null,
                    request.Headers.Contains("Idempotency-Key"),
                    request.Content is null
                        ? ""
                        : await request.Content.ReadAsStringAsync(cancellationToken)));

            return _respond(request);
        }
    }
}
