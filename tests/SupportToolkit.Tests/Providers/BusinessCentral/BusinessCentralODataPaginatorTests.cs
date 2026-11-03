using System.Net;
using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralODataPaginatorTests
{
    [Fact]
    public async Task FollowsNextLinks_AndCombinesPages()
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Page(["a", "b"], NextLink("p2")))
                .Enqueue(() => Page(["c"], null));

        var items = await Fetch(network);

        Assert.Equal(["a", "b", "c"], items);
        Assert.Equal(2, network.Requests.Count);
    }

    [Fact]
    public async Task EmptyCollection_IsValidEmptyResult()
    {
        var items =
            await Fetch(
                new StubNetwork().Enqueue(() => Page([], null)));

        Assert.Empty(items);
    }

    [Fact]
    public async Task MissingValueCollection_IsContractFailure()
    {
        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => Fetch(
                new StubNetwork().Enqueue(() => Json("{}"))));
    }

    [Fact]
    public async Task FailureOnLaterPage_ThrowsInsteadOfReturningPartial()
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Page(["a"], NextLink("p2")))
                .Enqueue(() => Json("{}", HttpStatusCode.Forbidden));

        await Assert.ThrowsAsync<BusinessCentralApiException>(
            () => Fetch(network));
    }

    [Fact]
    public async Task RepeatedNextLink_IsRejected()
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Page(["a"], NextLink("loop")))
                .Enqueue(() => Page(["b"], NextLink("loop")));

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => Fetch(network));
    }

    [Theory]
    [InlineData("https://attacker.example/v2.0/x")]
    [InlineData("http://api.businesscentral.dynamics.com/v2.0/x")]
    [InlineData("https://api.businesscentral.dynamics.com:8443/v2.0/x")]
    [InlineData("https://user@api.businesscentral.dynamics.com/v2.0/x")]
    [InlineData("https://api.businesscentral.dynamics.com/v2.0/other/path")]
    [InlineData("relative/path")]
    public async Task UnsafeNextLinks_AreRejectedBeforeBeingFollowed(
        string nextLink)
    {
        var network =
            new StubNetwork()
                .Enqueue(() => Page(["a"], nextLink));

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => Fetch(network));

        Assert.Single(network.Requests);
    }

    [Fact]
    public async Task EndlessFreshLinks_AreBoundedByPageCap()
    {
        var network = new StubNetwork();
        var counter = 0;

        for (var i = 0; i < 250; i++)
        {
            network.Enqueue(
                () => Page(
                    ["x"],
                    NextLink("p" + counter++)));
        }

        var exception =
            await Assert.ThrowsAsync<BusinessCentralContractException>(
                () => Fetch(network));

        Assert.Contains("200 pages", exception.Message);
        Assert.Equal(200, network.Requests.Count);
    }

    [Fact]
    public async Task NextLink_IsOnlyFollowedWhenPathIsAllowlisted()
    {
        // The server points at an in-origin page that is not approved.
        var network =
            new StubNetwork()
                .Enqueue(() => Page(
                    ["a"],
                    Link("companies(" + CompanyId + ")/unapproved")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Fetch(network));

        Assert.Single(network.Requests);
    }

    private static string NextLink(
        string token)
    {
        return Link(CollectionPath + "?$skiptoken=" + token);
    }

    private static Task<IReadOnlyList<string>> Fetch(
        StubNetwork network)
    {
        var client =
            new BusinessCentralApiClient(
                Client(
                    new FakeTokenProvider(),
                    network,
                    CollectionPath));

        return new BusinessCentralODataPaginator(client)
            .FetchAllAsync<string>(
                "synthetic items",
                CollectionPath);
    }

    private static HttpResponseMessage Page(
        string[] values,
        string? nextLink)
    {
        var items =
            string.Join(",", values.Select(v => $"\"{v}\""));

        var link =
            nextLink is null
                ? string.Empty
                : $",\"@odata.nextLink\":\"{nextLink}\"";

        return Json($"{{\"value\":[{items}]{link}}}");
    }
}
