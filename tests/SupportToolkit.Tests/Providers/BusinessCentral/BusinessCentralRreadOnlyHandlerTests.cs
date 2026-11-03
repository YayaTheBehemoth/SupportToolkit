
using System.Net;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Tests;

public class BusinessCentralReadOnlyHandlerTests
{
    private const string BaseUrl =
        "https://bc.fixture.invalid/api/";

    private const string FixturePath =
        "fixture/monitoring-scope";

    [Fact]
    public async Task ApprovedGet_IsSent()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        using var response =
            await client.GetAsync(
                FixturePath
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
    public async Task QueryOnApprovedPath_IsAllowed()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        using var response =
            await client.GetAsync(
                FixturePath + "?limit=1"
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
    public async Task DefaultAllowlist_BlocksAllGets()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(FixturePath)
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingMethods_AreBlocked(
        string method)
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                FixturePath
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request)
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task UnknownGetEndpoint_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "fixture/other-endpoint"
            )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task DifferentHost_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "https://other.fixture.invalid/api/" + FixturePath
            )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task DifferentPort_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "https://bc.fixture.invalid:8443/api/" + FixturePath
            )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task InsecureHttp_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(
                "http://bc.fixture.invalid/api/" + FixturePath
            )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task GetWithBody_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(
                network,
                [FixturePath]
            );

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                FixturePath
            )
            {
                Content = new StringContent(
                    "not allowed"
                )
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request)
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Theory]
    [InlineData("../different")]
    [InlineData("/absolute")]
    [InlineData("fixture/monitoring-scope?expand=all")]
    public void UnsafeAllowlistPaths_AreRejected(
        string path)
    {
        using var network =
            new RecordingHandler();

        Assert.Throws<ArgumentException>(
            () => new BusinessCentralReadOnlyHandler(
                BaseUrl,
                network,
                [path]
            )
        );
    }

    private static HttpClient CreateClient(
        RecordingHandler network,
        IEnumerable<string>? allowedPaths = null)
    {
        var handler =
            new BusinessCentralReadOnlyHandler(
                BaseUrl,
                network,
                allowedPaths
            );

        return new HttpClient(
            handler
        )
        {
            BaseAddress =
                new Uri(BaseUrl)
        };
    }

    private sealed class RecordingHandler
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

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
