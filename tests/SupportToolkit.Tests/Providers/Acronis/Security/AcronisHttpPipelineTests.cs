using System.Net;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests;

/// <summary>
/// The one Acronis pipeline carries the read-only guard: an approved read
/// reaches the network, a write never does.
/// </summary>
public class AcronisHttpPipelineTests
{
    private const string DatacenterUrl =
        "https://fixture.acronis.invalid";

    [Fact]
    public async Task ApprovedRead_ReachesTheNetwork()
    {
        var network = new RecordingHandler();

        using var client = Pipeline(network);

        using var response =
            await client.GetAsync(DatacenterUrl + "/api/2/tenants?limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, network.RequestCount);
    }

    [Fact]
    public async Task Write_IsBlockedBeforeTheNetwork()
    {
        var network = new RecordingHandler();

        using var client = Pipeline(network);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DeleteAsync(DatacenterUrl + "/api/2/tenants/fixture"));

        Assert.Equal(0, network.RequestCount);
    }

    [Fact]
    public void Client_HasTheEstablishedTimeout()
    {
        using var client = Pipeline(new RecordingHandler());

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
    }

    private static HttpClient Pipeline(HttpMessageHandler network) =>
        AcronisHttpPipeline.Create(
            AcronisOptions.Create(DatacenterUrl, "fixture-client", "fixture-secret"),
            network);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
