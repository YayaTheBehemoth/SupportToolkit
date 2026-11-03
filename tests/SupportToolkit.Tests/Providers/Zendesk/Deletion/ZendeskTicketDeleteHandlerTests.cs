using System.Net;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Deleting;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

/// <summary>
/// The deletion pipeline may delete tickets, but only exactly like this.
/// </summary>
public class ZendeskTicketDeleteHandlerTests
{
    private static ZendeskDeletionOptions Options() =>
        ZendeskDeletionOptions.Create("example", "example-public-client");

    private const string Host = "https://example.zendesk.com";

    [Theory]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1,2,3")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many?ids=12,15")]
    [InlineData("GET", "/api/v2/job_statuses/V3-abc123DEF.json")]
    [InlineData("GET", "/api/v2/job_statuses/J1.json")]
    public async Task TheTwoPermittedRequests_AreAllowed(
        string method,
        string pathAndQuery)
    {
        var network = new Recorder();
        var client = Deleter(network, new FakeTokens());

        using var request =
            new HttpRequestMessage(new HttpMethod(method), Host + pathAndQuery);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task AHundredIds_AreAllowed()
    {
        var network = new Recorder();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                Host + "/api/v2/tickets/destroy_many.json?ids=" +
                string.Join(",", Enumerable.Range(1, 100)));

        using var response = await Deleter(network, new FakeTokens()).SendAsync(request);

        Assert.Equal(1, network.Requests);
    }

    [Fact]
    public async Task MoreThanAHundredIds_AreBlocked()
    {
        await AssertBlocked(
            "DELETE",
            "/api/v2/tickets/destroy_many.json?ids=" +
            string.Join(",", Enumerable.Range(1, 101)));
    }

    [Theory]
    // Bad or ambiguous ID lists.
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1,,2")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1,1")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=0")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=-5")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=abc")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1;2")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1+2")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1.5")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=99999999999999999999")]
    // Extra parameters or a different parameter.
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?ids=1&force=true")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?force=1&ids=1")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json?id=1")]
    // Other deletion endpoints.
    [InlineData("DELETE", "/api/v2/tickets/1.json")]
    [InlineData("DELETE", "/api/v2/tickets/1")]
    [InlineData("DELETE", "/api/v2/deleted_tickets/destroy_many.json?ids=1")]
    [InlineData("DELETE", "/api/v2/tickets/destroy_many.json/extra?ids=1")]
    [InlineData("DELETE", "/api/v2/users/1.json")]
    [InlineData("DELETE", "/api/v2/organizations/1.json")]
    // Wrong methods on the permitted paths.
    [InlineData("GET", "/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("POST", "/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("PUT", "/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("DELETE", "/api/v2/job_statuses/J1.json")]
    [InlineData("POST", "/api/v2/job_statuses/J1.json")]
    // Job status variations.
    [InlineData("GET", "/api/v2/job_statuses/J1.json?x=1")]
    [InlineData("GET", "/api/v2/job_statuses/J1")]
    [InlineData("GET", "/api/v2/job_statuses/.json")]
    [InlineData("GET", "/api/v2/job_statuses/J1/extra.json")]
    [InlineData("GET", "/api/v2/job_statuses/..%2Fusers.json")]
    // Reads belong to the read-only pipeline, never this one.
    [InlineData("GET", "/api/v2/search.json?query=type%3Aticket")]
    [InlineData("GET", "/api/v2/users/me.json")]
    [InlineData("GET", "/api/v2/tickets/1.json")]
    [InlineData("GET", "/api/v2/organizations/show_many.json?ids=1")]
    // Other methods and the sign-in endpoint.
    [InlineData("POST", "/api/v2/tickets.json")]
    [InlineData("PATCH", "/api/v2/tickets/1.json")]
    [InlineData("GET", "/oauth/tokens")]
    public async Task EverythingElse_IsBlocked_BeforeAnyTokenIsRequested(
        string method,
        string pathAndQuery)
    {
        await AssertBlocked(method, pathAndQuery);
    }

    [Theory]
    [InlineData("https://other.zendesk.com/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("https://example.zendesk.com.evil.test/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("http://example.zendesk.com/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("https://user@example.zendesk.com/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("https://example.zendesk.com:8443/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("https://example.zendesk.com/api/v2/tickets/destroy_many.json?ids=1#frag")]
    public async Task WrongOriginOrFragment_IsBlocked(string url)
    {
        var network = new Recorder();
        var tokens = new FakeTokens();

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Deleter(network, tokens).SendAsync(request));

        Assert.Equal(0, network.Requests);
        Assert.Equal(0, tokens.Requests);
    }

    [Fact]
    public async Task ARequestWithABody_IsBlocked()
    {
        var network = new Recorder();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                Host + "/api/v2/tickets/destroy_many.json?ids=1")
            {
                Content = new StringContent("{}")
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Deleter(network, new FakeTokens()).SendAsync(request));

        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task TheReadOnlyPipeline_StillCannotDelete()
    {
        var network = new Recorder();

        var readOnly =
            ZendeskDeletionHttpPipeline.Create(
                Options(),
                new FakeTokens(),
                network);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                Host + "/api/v2/tickets/destroy_many.json?ids=1");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => readOnly.SendAsync(request));

        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task TheDeletionPipeline_AttachesTheTokenOnlyToAllowedRequests()
    {
        var network = new Recorder();
        var tokens = new FakeTokens();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                Host + "/api/v2/tickets/destroy_many.json?ids=4,5");

        using var response = await Deleter(network, tokens).SendAsync(request);

        Assert.Equal("Bearer example-token", network.LastAuthorization);
        Assert.Equal(1, tokens.Requests);
    }

    [Fact]
    public async Task Provider_SendsExactlyTheGivenIds_InOrder()
    {
        var network = new Recorder("""{"job_status":{"id":"J9","status":"queued"}}""");

        var provider =
            new ZendeskTicketDeletionProvider(
                new ZendeskDeletionApiClient(
                    Deleter(network, new FakeTokens()),
                    Options()));

        var job = await provider.StartDeleteAsync([30, 4, 17]);

        Assert.Equal("J9", job.JobId);
        Assert.Equal("queued", job.Status);
        Assert.True(job.IsRunning);
        Assert.Equal("DELETE", network.LastMethod);
        Assert.Equal(
            "/api/v2/tickets/destroy_many.json?ids=30,4,17",
            network.LastPathAndQuery);
    }

    [Theory]
    [MemberData(nameof(InvalidIdLists))]
    public async Task Provider_RefusesInvalidIdLists_WithoutSendingAnything(
        long[] ids)
    {
        var network = new Recorder();

        var provider =
            new ZendeskTicketDeletionProvider(
                new ZendeskDeletionApiClient(
                    Deleter(network, new FakeTokens()),
                    Options()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.StartDeleteAsync(ids));

        Assert.Equal(0, network.Requests);
    }

    public static TheoryData<long[]> InvalidIdLists() =>
        new()
        {
            Array.Empty<long>(),
            new long[] { 0 },
            new long[] { -1 },
            new long[] { 1, 1 },
            Enumerable.Range(1, 101).Select(i => (long)i).ToArray()
        };

    [Fact]
    public async Task Provider_ParsesAJobStatusWithPerTicketResults()
    {
        var network =
            new Recorder(
                """
                {"job_status":{"id":"J9","status":"Completed","total":2,"progress":2,
                 "message":null,"results":[
                   {"id":1,"action":"destroy","success":true,"status":"Deleted"},
                   {"id":2,"action":"destroy","success":false,"status":"Failed"},
                   {"id":null,"success":true}]}}
                """);

        var provider =
            new ZendeskTicketDeletionProvider(
                new ZendeskDeletionApiClient(
                    Deleter(network, new FakeTokens()),
                    Options()));

        var job = await provider.GetJobAsync("J9");

        Assert.Equal("completed", job.Status);
        Assert.True(job.IsFinished);
        Assert.Equal(2, job.Results.Count);
        Assert.True(job.Results[0].Success);
        Assert.False(job.Results[1].Success);
        Assert.Equal("/api/v2/job_statuses/J9.json", network.LastPathAndQuery);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("../users")]
    [InlineData("J 1")]
    public async Task Provider_RefusesAnInvalidJobId(string jobId)
    {
        var network = new Recorder();

        var provider =
            new ZendeskTicketDeletionProvider(
                new ZendeskDeletionApiClient(
                    Deleter(network, new FakeTokens()),
                    Options()));

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetJobAsync(jobId));

        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task Provider_RejectsAResponseWithoutAJob()
    {
        var network = new Recorder("{}");

        var provider =
            new ZendeskTicketDeletionProvider(
                new ZendeskDeletionApiClient(
                    Deleter(network, new FakeTokens()),
                    Options()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.StartDeleteAsync([1]));
    }

    private async Task AssertBlocked(
        string method,
        string pathAndQuery)
    {
        var network = new Recorder();
        var tokens = new FakeTokens();

        using var request =
            new HttpRequestMessage(new HttpMethod(method), Host + pathAndQuery);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Deleter(network, tokens).SendAsync(request));

        // Nothing reached the network, and no sign-in was triggered.
        Assert.Equal(0, network.Requests);
        Assert.Equal(0, tokens.Requests);
    }

    private static HttpClient Deleter(
        Recorder network,
        FakeTokens tokens) =>
        ZendeskDeletionHttpPipeline.CreateDeleter(Options(), tokens, network);

    private sealed class FakeTokens : IZendeskInvalidatableTokenSource
    {
        public int Requests { get; private set; }

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult("example-token");
        }

        public void Invalidate()
        {
        }
    }

    private sealed class Recorder : HttpMessageHandler
    {
        private readonly string _body;

        public Recorder(string body = "{}")
        {
            _body = body;
        }

        public int Requests { get; private set; }

        public string? LastMethod { get; private set; }

        public string? LastPathAndQuery { get; private set; }

        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastMethod = request.Method.Method;
            LastPathAndQuery = request.RequestUri!.PathAndQuery;
            LastAuthorization = request.Headers.Authorization?.ToString();

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        _body,
                        System.Text.Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
