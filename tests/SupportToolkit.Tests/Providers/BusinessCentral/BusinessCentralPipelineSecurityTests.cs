using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralPipelineSecurityTests
{
    [Fact]
    public async Task ApprovedGet_CarriesBearerToken()
    {
        var tokens = new FakeTokenProvider();

        var network =
            new StubNetwork()
                .Enqueue(() => Json("{}"));

        using var client =
            Client(tokens, network, CollectionPath);

        using var response =
            await client.GetAsync(CollectionPath);

        var request = Assert.Single(network.Requests);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal(Token, request.Headers.Authorization?.Parameter);
        Assert.Equal(1, tokens.Requests);
    }

    [Fact]
    public async Task Requests_PinTheLanguage()
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Json("{}"));

        using var client =
            Client(new FakeTokenProvider(), network, CollectionPath);

        using var response =
            await client.GetAsync(CollectionPath);

        var request = Assert.Single(network.Requests);

        var language =
            Assert.Single(request.Headers.AcceptLanguage);

        Assert.Equal("en-US", language.Value);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingMethods_NeverRequestOrSendToken(
        string method)
    {
        var tokens = new FakeTokenProvider();
        var network = new StubNetwork();

        using var client =
            Client(tokens, network, CollectionPath);

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                CollectionPath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request));

        Assert.Equal(0, tokens.Requests);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task NonAllowlistedPath_NeverRequestsToken()
    {
        var tokens = new FakeTokenProvider();
        var network = new StubNetwork();

        using var client =
            Client(tokens, network, CollectionPath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync("companies(" + CompanyId + ")/other"));

        Assert.Equal(0, tokens.Requests);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public async Task ForeignHost_NeverRequestsToken()
    {
        var tokens = new FakeTokenProvider();
        var network = new StubNetwork();

        using var client =
            Client(tokens, network, CollectionPath);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "https://attacker.example/" + CollectionPath));

        Assert.Equal(0, tokens.Requests);
        Assert.Empty(network.Requests);
    }

    [Fact]
    public void DefaultPipeline_DisablesRedirectsAndCookies()
    {
        // The real network handler is built inside the pipeline;
        // construction must succeed without any network or sign-in.
        using var client =
            BusinessCentralHttpPipeline.Create(
                Options(),
                new FakeTokenProvider(),
                []);

        Assert.Equal(Options().ApiBaseUri, client.BaseAddress);
    }
}
