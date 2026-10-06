using System.Net;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class AcronisReadOnlyHierarchyTests
{
    private const string DatacenterUrl =
        "https://fixture.acronis.invalid";

    [Fact]
    public async Task RootBootstrapWithUuid_IsAllowed()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network
            );

        using var response =
            await client.GetAsync(
                "/api/1/groups/" +
                "11111111-1111-1111-1111-111111111111"
            );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        Assert.Equal(
            1,
            network.RequestCount
        );
    }

    [Fact]
    public async Task NumericHierarchyChildrenEndpoint_IsAllowed()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network
            );

        using var response =
            await client.GetAsync(
                "/api/1/groups/123456/children"
            );

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        Assert.Equal(
            1,
            network.RequestCount
        );
    }

    [Fact]
    public async Task UuidHierarchyChildrenEndpoint_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.GetAsync(
                    "/api/1/groups/" +
                    "11111111-1111-1111-1111-111111111111/" +
                    "children"
                )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task ArbitraryNumericGroupLookupWithoutChildren_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.GetAsync(
                    "/api/1/groups/123456"
                )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    private static HttpClient CreateClient(
        RecordingHandler network)
    {
        var guard =
            new AcronisReadOnlyHandler(
                DatacenterUrl,
                network
            );

        return new HttpClient(
            guard
        )
        {
            BaseAddress =
                new Uri(
                    DatacenterUrl
                )
        };
    }

    private sealed class RecordingHandler
        : HttpMessageHandler
    {
        public int RequestCount
        {
            get;
            private set;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            return Task.FromResult(
                new HttpResponseMessage(
                    HttpStatusCode.OK
                )
            );
        }
    }
}