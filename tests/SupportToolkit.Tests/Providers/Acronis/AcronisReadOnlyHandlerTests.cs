using System.Net;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class AcronisReadOnlyHandlerTests
{
    private const string DatacenterUrl =
        "https://fixture.acronis.invalid";

    [Theory]
    [InlineData("/api/2/clients/fixture-client")]
    [InlineData("/api/2/tenants?limit=100")]
    [InlineData("/api/resource_management/v4/resource_statuses?limit=100")]
    [InlineData("/api/alert_manager/v1/alerts?show_deleted=false")]
    public async Task AllowedReadEndpoints_AreSent(
        string path)
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        using var response =
            await client.GetAsync(path);

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
    public async Task TokenPost_IsAllowed()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/2/idp/token"
            )
            {
                Content =
                    new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["grant_type"] =
                                "client_credentials"
                        }
                    )
            };

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode
        );

        Assert.Equal(
            1,
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
            CreateClient(network);

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                "/api/resource_management/v4/resource_statuses"
            );

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    client.SendAsync(request)
            );

        Assert.Contains(
            "read-only",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task PostToReadEndpoint_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/2/tenants"
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.SendAsync(request)
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
            CreateClient(network);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    client.GetAsync(
                        "/api/something/not-approved"
                    )
            );

        Assert.Contains(
            "read-only",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
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
            CreateClient(network);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    client.GetAsync(
                        "https://example.com/api/2/tenants"
                    )
            );

        Assert.Contains(
            "unexpected origin",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
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
            CreateClient(network);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    client.GetAsync(
                        "https://fixture.acronis.invalid:8443/api/2/tenants"
                    )
            );

        Assert.Contains(
            "unexpected origin",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task HttpRequest_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    client.GetAsync(
                        "http://fixture.acronis.invalid/api/2/tenants"
                    )
            );

        Assert.Contains(
            "HTTPS",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task GetRequestWithBody_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/2/tenants"
            )
            {
                Content =
                    new StringContent(
                        "this should never be sent"
                    )
            };

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.SendAsync(request)
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task TokenEndpointWithGet_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.GetAsync(
                    "/api/2/idp/token"
                )
        );

        Assert.Equal(
            0,
            network.RequestCount
        );
    }

    [Fact]
    public async Task PostToUnknownEndpoint_IsBlocked()
    {
        var network =
            new RecordingHandler();

        using var client =
            CreateClient(network);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/definitely-not-approved"
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                client.SendAsync(request)
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