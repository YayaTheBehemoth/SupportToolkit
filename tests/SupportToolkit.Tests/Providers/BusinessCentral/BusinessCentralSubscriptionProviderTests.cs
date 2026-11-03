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

    // ---- Billing read (usage report) ----

    private const string BillingJson = """
        {"value":[
          {"subscriptionNumber":"000000001","lineNumber":10000,"type":"Item",
           "number":"EXAMPLE-ITEM","description":"Example backup storage",
           "unitOfMeasureCode":"GB","quantity":500,"unitPrice":1.25,
           "lineDiscountPercent":10,"amount":562.5,"omitLine":false}]}
        """;

    [Fact]
    public async Task BillingRead_UsesTheApprovedLinesPage_WithItsOwnSelect()
    {
        var network =
            new StubNetwork().Enqueue(() => Json(BillingJson));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        var line =
            Assert.Single(await provider.GetBillingLinesAsync())!;

        var request = Assert.Single(network.Requests);

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.EndsWith(
            "/companies(" + CompanyId + ")/smartSubscriptionLines",
            request.RequestUri!.AbsolutePath);
        Assert.Contains(
            "$select=" + BusinessCentralSubscriptionFields.BillingLineSelect,
            Uri.UnescapeDataString(request.RequestUri.Query));
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);

        Assert.Equal("000000001", line.SubscriptionNumber);
        Assert.Equal(10000, line.LineNumber);
        Assert.Equal("EXAMPLE-ITEM", line.Number);
        Assert.Equal("Example backup storage", line.Description);
        Assert.Equal("GB", line.UnitOfMeasureCode);
        Assert.Equal(500m, line.Quantity);
        Assert.Equal(1.25m, line.UnitPrice);
        Assert.Equal(10m, line.LineDiscountPercent);
        Assert.Equal(562.5m, line.Amount);
        Assert.False(line.OmitLine);
    }

    [Fact]
    public async Task BillingRead_MissingValues_AreUnknown_NotZero()
    {
        var network =
            new StubNetwork().Enqueue(
                () => Json(
                    "{\"value\":[{\"subscriptionNumber\":\"000000001\"," +
                    "\"number\":\"EXAMPLE-ITEM\"}]}"));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        var line =
            Assert.Single(await provider.GetBillingLinesAsync())!;

        Assert.Null(line.Amount);
        Assert.Null(line.UnitPrice);
        Assert.Null(line.Quantity);
        Assert.Null(line.Description);
    }

    [Fact]
    public async Task BillingRead_Failure_Throws_InsteadOfReturningPartLines()
    {
        // For example a price column that has been renamed.
        var network =
            new StubNetwork().Enqueue(
                () => Json("{}", HttpStatusCode.BadRequest));

        var provider =
            BusinessCentralSubscriptionProvider.Create(
                Options(),
                new FakeTokenProvider(),
                networkHandler: network);

        await Assert.ThrowsAsync<BusinessCentralApiException>(
            () => provider.GetBillingLinesAsync());
    }

    [Theory]
    [InlineData("price")]
    [InlineData("amount")]
    [InlineData("discount")]
    [InlineData("description")]
    public void TheEntitlementRead_NeverAsksForBillingColumns(
        string column)
    {
        Assert.DoesNotContain(
            column,
            BusinessCentralSubscriptionFields.HeaderSelect,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            column,
            BusinessCentralSubscriptionFields.LineSelect,
            StringComparison.OrdinalIgnoreCase);
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
