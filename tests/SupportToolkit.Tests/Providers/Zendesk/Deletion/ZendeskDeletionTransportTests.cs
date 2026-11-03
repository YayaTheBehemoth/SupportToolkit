using System.Net;
using System.Net.Http.Headers;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Search.Dtos;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

public class ZendeskDeletionTransportTests
{
    private static ZendeskDeletionOptions Options() =>
        ZendeskDeletionOptions.Create("example", "example-public-client");

    [Theory]
    [InlineData("/api/v2/search.json?query=type%3Aticket")]
    [InlineData("/api/v2/users/me.json")]
    [InlineData("/api/v2/users/show_many.json?ids=1,2")]
    [InlineData("/api/v2/organizations/show_many.json?ids=1,2")]
    public async Task AllowlistedGets_ArePermitted(string pathAndQuery)
    {
        var network = new RecordingHandler();
        var client = Pipeline(network, new FakeTokenSource());

        using var response =
            await client.GetAsync("https://example.zendesk.com" + pathAndQuery);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, network.Requests);
    }

    [Theory]
    [InlineData("GET", "https://example.zendesk.com/api/v2/tickets/1.json")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/tickets.json")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/search/export.json")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/search.json/extra")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/search.json#frag")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/users.json")]
    [InlineData("GET", "https://example.zendesk.com/oauth/tokens")]
    [InlineData("GET", "https://example.zendesk.com/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("DELETE", "https://example.zendesk.com/api/v2/tickets/destroy_many.json?ids=1")]
    [InlineData("DELETE", "https://example.zendesk.com/api/v2/tickets/1.json")]
    [InlineData("POST", "https://example.zendesk.com/api/v2/tickets.json")]
    [InlineData("POST", "https://example.zendesk.com/api/v2/search.json")]
    [InlineData("PUT", "https://example.zendesk.com/api/v2/search.json")]
    [InlineData("PATCH", "https://example.zendesk.com/api/v2/users/me.json")]
    [InlineData("GET", "https://other.zendesk.com/api/v2/search.json")]
    [InlineData("GET", "https://example.zendesk.com.evil.test/api/v2/search.json")]
    [InlineData("GET", "http://example.zendesk.com/api/v2/search.json")]
    [InlineData("GET", "https://user@example.zendesk.com/api/v2/search.json")]
    [InlineData("GET", "https://example.zendesk.com:8443/api/v2/search.json")]
    public async Task EverythingElse_IsBlocked_BeforeAnyTokenIsRequested(
        string method,
        string url)
    {
        var network = new RecordingHandler();
        var tokens = new FakeTokenSource();
        var client = Pipeline(network, tokens);

        using var request =
            new HttpRequestMessage(new HttpMethod(method), url);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));

        Assert.Equal(0, network.Requests);

        // A blocked request never triggers sign-in or carries a token.
        Assert.Equal(0, tokens.Requests);
    }

    [Fact]
    public async Task AGetWithABody_IsBlocked()
    {
        var network = new RecordingHandler();
        var client = Pipeline(network, new FakeTokenSource());

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "https://example.zendesk.com/api/v2/search.json")
            {
                Content = new StringContent("{}")
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));

        Assert.Equal(0, network.Requests);
    }

    [Fact]
    public async Task AllowedRequests_CarryTheBearerToken()
    {
        var network = new RecordingHandler();
        var client = Pipeline(network, new FakeTokenSource());

        using var response =
            await client.GetAsync(
                "https://example.zendesk.com/api/v2/users/me.json");

        Assert.Equal("Bearer", network.LastAuthorization?.Scheme);
        Assert.Equal("example-token", network.LastAuthorization?.Parameter);
    }

    [Fact]
    public async Task AnUnauthorizedResponse_DiscardsTheToken()
    {
        var network =
            new RecordingHandler(HttpStatusCode.Unauthorized);

        var tokens = new FakeTokenSource();
        var client = Pipeline(network, tokens);

        using var response =
            await client.GetAsync(
                "https://example.zendesk.com/api/v2/users/me.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, tokens.Invalidations);
    }

    [Fact]
    public async Task ApiClient_SendsOnlyTheRequestedRoute_ToTheConfiguredInstance()
    {
        var network = new RecordingHandler(body: """{"user":{"id":7}}""");
        var client = Pipeline(network, new FakeTokenSource());

        var api =
            new ZendeskDeletionApiClient(client, Options());

        var dto =
            await api.GetJsonAsync<ZendeskCurrentUserDto>(
                "/api/v2/users/me.json");

        Assert.Equal(7, dto.User!.Id);
        Assert.Equal("example.zendesk.com", network.LastUri!.Host);
        Assert.Equal("/api/v2/users/me.json", network.LastUri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Sign in again")]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    public async Task Failures_AreDescribed_WithoutTheResponseBody(
        HttpStatusCode status,
        string expected)
    {
        var network =
            new RecordingHandler(status, "customer-data-in-error-body");

        var api =
            new ZendeskDeletionApiClient(
                Pipeline(network, new FakeTokenSource()),
                Options());

        var exception =
            await Assert.ThrowsAsync<ZendeskApiException>(
                () => api.GetJsonAsync<ZendeskCurrentUserDto>(
                    "/api/v2/users/me.json"));

        Assert.Equal(status, exception.StatusCode);
        Assert.Contains(expected, exception.Message);
        Assert.DoesNotContain(
            "customer-data-in-error-body",
            exception.Message);
    }

    [Fact]
    public async Task RateLimit_MentionsRetryAfter_AndIsNotRetriedAutomatically()
    {
        var network =
            new RecordingHandler(
                HttpStatusCode.TooManyRequests,
                retryAfter: TimeSpan.FromSeconds(30));

        var api =
            new ZendeskDeletionApiClient(
                Pipeline(network, new FakeTokenSource()),
                Options());

        var exception =
            await Assert.ThrowsAsync<ZendeskApiException>(
                () => api.GetJsonAsync<ZendeskCurrentUserDto>(
                    "/api/v2/users/me.json"));

        Assert.Contains("30 seconds", exception.Message);
        Assert.Equal(1, network.Requests);
    }

    [Fact]
    public void TheTwoConnections_UseDistinctClientsAndTokenSources()
    {
        // Ticketing authenticates with client credentials and a secret;
        // deletion with an interactive public-client sign-in. Neither
        // type can stand in for the other.
        Assert.False(
            typeof(IZendeskInvalidatableTokenSource)
                .IsAssignableFrom(typeof(ZendeskClientCredentialsTokenSource)));

        Assert.False(
            typeof(ZendeskClientCredentialsTokenSource)
                .IsAssignableFrom(typeof(ZendeskPkceTokenSource)));

        Assert.NotEqual(
            typeof(ZendeskOptions),
            typeof(ZendeskDeletionOptions));

        // The deletion options have no secret to carry.
        Assert.DoesNotContain(
            typeof(ZendeskDeletionOptions).GetProperties(),
            property => property.Name.Contains(
                "Secret",
                StringComparison.OrdinalIgnoreCase));

        // The ticketing client has no read or search surface.
        Assert.DoesNotContain(
            typeof(ZendeskApiClient).GetMethods(),
            method => method.Name.Contains(
                "Search",
                StringComparison.OrdinalIgnoreCase)
                || method.Name.Contains(
                    "Delete",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Options_RejectBadSubdomains_AndHaveNoSecret()
    {
        Assert.Throws<InvalidOperationException>(
            () => ZendeskDeletionOptions.Create(
                "evil.test/",
                "example-public-client"));

        Assert.Throws<InvalidOperationException>(
            () => ZendeskDeletionOptions.Create("example", " "));

        Assert.Equal(
            "https://example.zendesk.com/",
            Options().BaseUri.ToString());

        Assert.Equal("read tickets:write", ZendeskDeletionOptions.SignInScope);
    }

    private static HttpClient Pipeline(
        RecordingHandler network,
        FakeTokenSource tokens) =>
        ZendeskDeletionHttpPipeline.Create(Options(), tokens, network);

    private sealed class FakeTokenSource : IZendeskInvalidatableTokenSource
    {
        public int Requests { get; private set; }

        public int Invalidations { get; private set; }

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult("example-token");
        }

        public void Invalidate() => Invalidations++;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly TimeSpan? _retryAfter;

        public RecordingHandler(
            HttpStatusCode status = HttpStatusCode.OK,
            string body = "{}",
            TimeSpan? retryAfter = null)
        {
            _status = status;
            _body = body;
            _retryAfter = retryAfter;
        }

        public int Requests { get; private set; }

        public Uri? LastUri { get; private set; }

        public AuthenticationHeaderValue? LastAuthorization
        {
            get;
            private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.Authorization;

            var response =
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(
                        _body,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

            if (_retryAfter is not null)
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(_retryAfter.Value);
            }

            return Task.FromResult(response);
        }
    }
}
