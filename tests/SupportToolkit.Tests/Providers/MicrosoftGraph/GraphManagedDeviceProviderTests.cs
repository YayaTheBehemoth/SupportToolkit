
using System.Net;
using System.Text;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

namespace SupportToolkit.Tests.Providers.MicrosoftGraph;

public class GraphManagedDeviceProviderTests
{
    [Fact]
    public async Task GetManagedDevicesAsync_ParsesDevice()
    {
        using var client = CreateClient(
            _ => JsonResponse(
                """
                {
                  "value": [
                    {
                      "id": "device-001",
                      "deviceName": "STK-SYNTHETIC-STALE-01",
                      "operatingSystem": "Windows",
                      "complianceState": "noncompliant",
                      "lastSyncDateTime": "2026-08-01T09:00:00Z"
                    }
                  ]
                }
                """
            )
        );

        var provider = new GraphManagedDeviceProvider(client);

        var devices = await provider.GetManagedDevicesAsync();

        var device = Assert.Single(devices);

        Assert.Equal("device-001", device.Id);
        Assert.Equal(
            "STK-SYNTHETIC-STALE-01",
            device.DeviceName
        );
        Assert.Equal("noncompliant", device.ComplianceState);
        Assert.Equal(
            new DateTimeOffset(
                2026, 8, 1, 9, 0, 0, TimeSpan.Zero
            ),
            device.LastSyncDateTime
        );
    }

    [Fact]
    public async Task GetManagedDevicesAsync_FollowsPagination()
    {
        var requests = new List<string>();

        using var client = CreateClient(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);

            var query = request.RequestUri!.Query;
            requests.Add(query);

            if (query == "?$top=5")
            {
                return JsonResponse(
                    """
                    {
                      "@odata.nextLink":
                        "https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=second",
                      "value": [
                        {
                          "id": "device-001",
                          "deviceName": "DEVICE-01"
                        }
                      ]
                    }
                    """
                );
            }

            if (query == "?$skiptoken=second")
            {
                return JsonResponse(
                    """
                    {
                      "value": [
                        {
                          "id": "device-002",
                          "deviceName": "DEVICE-02"
                        }
                      ]
                    }
                    """
                );
            }

            return new HttpResponseMessage(
                HttpStatusCode.NotFound
            );
        });

        var provider = new GraphManagedDeviceProvider(client);

        var devices = await provider.GetManagedDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal(2, requests.Count);
        Assert.Equal("DEVICE-01", devices[0].DeviceName);
        Assert.Equal("DEVICE-02", devices[1].DeviceName);
    }

    [Fact]
    public async Task GetManagedDevicesAsync_RejectsUntrustedNextLink()
    {
        var requestCount = 0;

        using var client = CreateClient(_ =>
        {
            requestCount++;

            return JsonResponse(
                """
                {
                  "@odata.nextLink":
                    "https://example.com/untrusted",
                  "value": []
                }
                """
            );
        });

        var provider = new GraphManagedDeviceProvider(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetManagedDevicesAsync()
        );

        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task GetManagedDevicesAsync_RejectsRepeatedPage()
    {
        using var client = CreateClient(
            _ => JsonResponse(
                """
                {
                  "@odata.nextLink":
                    "https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$top=5",
                  "value": []
                }
                """
            )
        );

        var provider = new GraphManagedDeviceProvider(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.GetManagedDevicesAsync()
        );
    }

    [Fact]
    public async Task GetManagedDevicesAsync_PropagatesHttpFailure()
    {
        using var client = CreateClient(
            _ => new HttpResponseMessage(
                HttpStatusCode.Forbidden
            )
        );

        var provider = new GraphManagedDeviceProvider(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetManagedDevicesAsync()
        );
    }

    [Fact]
    public async Task GetManagedDevicesAsync_RejectsMissingValue()
    {
        using var client = CreateClient(
            _ => JsonResponse(
                """
                {
                  "@odata.context": "synthetic-context"
                }
                """
            )
        );

        var provider = new GraphManagedDeviceProvider(client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.GetManagedDevicesAsync()
        );
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        return new HttpClient(
            new StubHttpMessageHandler(response)
        );
    }

    private static HttpResponseMessage JsonResponse(
        string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            HttpResponseMessage
        > _response;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response(request));
        }
    }
}
