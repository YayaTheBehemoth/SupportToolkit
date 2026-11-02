using System.Net;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskTokenSourceTests
{
    private static ZendeskOptions Options() =>
        ZendeskOptions.Create("example", "example-client", "example-secret");

    private const string TokenJson =
        """{"access_token":"example-token","token_type":"bearer","expires_in":3600}""";

    [Fact]
    public async Task ClientCredentialsSource_CachesTheToken_AndRequestsOnlyTicketsWrite()
    {
        var tokenRequests = 0;
        string? body = null;

        var http =
            ZendeskTestHttpSupport.CreateClient(
                async request =>
                {
                    tokenRequests++;
                    Assert.Equal("/oauth/tokens", request.RequestUri!.AbsolutePath);
                    body = await request.Content!.ReadAsStringAsync();

                    return ZendeskTestHttpSupport.JsonResponse(
                        HttpStatusCode.OK,
                        TokenJson);
                });

        var source =
            new ZendeskClientCredentialsTokenSource(http, Options());

        Assert.Equal("example-token", await source.GetAccessTokenAsync());
        Assert.Equal("example-token", await source.GetAccessTokenAsync());

        Assert.Equal(1, tokenRequests);
        Assert.Contains("client_credentials", body);
        Assert.Contains("tickets:write", body);
    }

    [Theory]
    [InlineData("""{"access_token":"","token_type":"bearer","expires_in":3600}""")]
    [InlineData("""{"access_token":"x","token_type":"mac","expires_in":3600}""")]
    [InlineData("""{"access_token":"x","token_type":"bearer","expires_in":0}""")]
    public async Task ClientCredentialsSource_RejectsAnInvalidTokenResponse(string json)
    {
        var http =
            ZendeskTestHttpSupport.CreateClient(
                _ => Task.FromResult(
                    ZendeskTestHttpSupport.JsonResponse(HttpStatusCode.OK, json)));

        var source =
            new ZendeskClientCredentialsTokenSource(http, Options());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.GetAccessTokenAsync());
    }

    [Fact]
    public async Task ApiClient_UsesTheInjectedTokenSource_WithoutTouchingTheTokenEndpoint()
    {
        string? authorization = null;

        var http =
            ZendeskTestHttpSupport.CreateClient(
                request =>
                {
                    Assert.Equal(
                        "/api/v2/tickets.json",
                        request.RequestUri!.AbsolutePath);

                    authorization = request.Headers.Authorization!.ToString();

                    return Task.FromResult(
                        ZendeskTestHttpSupport.JsonResponse(
                            HttpStatusCode.Created,
                            "{}"));
                });

        var client =
            new ZendeskApiClient(
                http,
                Options(),
                new FixedTokenSource("injected-token"));

        using var response =
            await client.PostTicketAsync(
                new { ticket = new { subject = "example" } },
                "example-key");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Bearer injected-token", authorization);
    }

    [Fact]
    public void ApiClient_RequiresATokenSource()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ZendeskApiClient(
                new HttpClient(),
                Options(),
                (IZendeskAccessTokenSource)null!));
    }

    private sealed class FixedTokenSource : IZendeskAccessTokenSource
    {
        private readonly string _token;

        public FixedTokenSource(string token)
        {
            _token = token;
        }

        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_token);
    }
}
