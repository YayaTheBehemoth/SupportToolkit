using System.Net;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralSubscriptionProviderTests
{
    private const string HeaderJson = """
        {"value":[{"id":"x","number":"000000001","status":"Active"}]}
        """;

    private const string LineJson = """
        {"value":[{"id":"y","subscriptionNumber":"000000001","type":"Item"}]}
        """;

    [Fact]
    public async Task ReadsHeadersThenLines_WithSelectOnApprovedPathsOnly()
    {
        var tokens = new FakeTokenProvider();

        var network =
            new StubNetwork()
                .Enqueue(() => Json(HeaderJson))
                .Enqueue(() => Json(LineJson));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                tokens,
                networkHandler: network);

        var data = await provider.GetSubscriptionsAsync();

        Assert.Equal("000000001", Assert.Single(data.Headers)!.Number);
        Assert.Equal("Item", Assert.Single(data.Lines)!.Type);

        Assert.Equal(2, network.Requests.Count);
        Assert.All(network.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));

        var header = network.Requests[0].RequestUri!;
        var line = network.Requests[1].RequestUri!;

        Assert.EndsWith(
            "/companies(" + CompanyId + ")/smartSubscriptionHeaders",
            header.AbsolutePath);
        Assert.EndsWith(
            "/companies(" + CompanyId + ")/smartSubscriptionLines",
            line.AbsolutePath);

        Assert.Contains(
            "$select=" + BusinessCentralSubscriptionFields.HeaderSelect,
            Uri.UnescapeDataString(header.Query));
        Assert.Contains(
            "$select=" + BusinessCentralSubscriptionFields.LineSelect,
            Uri.UnescapeDataString(line.Query));

        Assert.All(
            network.Requests,
            r => Assert.Equal("Bearer", r.Headers.Authorization?.Scheme));
    }

    [Fact]
    public void ApprovedPaths_AreExactlyTheTwoPages()
    {
        Assert.Equal(
            [
                "companies(" + CompanyId + ")/smartSubscriptionHeaders",
                "companies(" + CompanyId + ")/smartSubscriptionLines"
            ],
            BusinessCentralSubscriptionProvider.ApprovedPaths(Options()));
    }

    [Fact]
    public async Task LineFailure_ThrowsInsteadOfReturningHeadersOnly()
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Json(HeaderJson))
                .Enqueue(() => Json("{}", HttpStatusCode.Forbidden));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        await Assert.ThrowsAsync<BusinessCentralApiException>(
            () => provider.GetSubscriptionsAsync());
    }

    [Fact]
    public async Task PagedHeaders_AreCombined()
    {
        var next =
            Link("companies(" + CompanyId + ")/smartSubscriptionHeaders?$skiptoken=2");

        var network =
            new StubNetwork()
                .Enqueue(() => Json(
                    "{\"value\":[{\"id\":\"a\",\"number\":\"1\"}]," +
                    "\"@odata.nextLink\":\"" + next + "\"}"))
                .Enqueue(() => Json(
                    "{\"value\":[{\"id\":\"b\",\"number\":\"2\"}]}"))
                .Enqueue(() => Json("{\"value\":[]}"));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        var data = await provider.GetSubscriptionsAsync();

        Assert.Equal(2, data.Headers.Count);
        Assert.Empty(data.Lines);
        Assert.Equal(3, network.Requests.Count);
    }

    [Fact]
    public async Task MalformedHeaderPage_IsContractFailure()
    {
        var network =
            new StubNetwork().Enqueue(() => Json("{\"value\":null}"));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => provider.GetSubscriptionsAsync());
    }

    [Fact]
    public void ProviderContract_ReturnsNoModuleTypes()
    {
        // The seam stays module independent: only provider DTOs.
        var data =
            typeof(BusinessCentralSubscriptionData)
                .GetProperties()
                .Select(p => p.PropertyType.ToString());

        Assert.DoesNotContain(
            data,
            type => type.Contains("Modules"));
    }
}
