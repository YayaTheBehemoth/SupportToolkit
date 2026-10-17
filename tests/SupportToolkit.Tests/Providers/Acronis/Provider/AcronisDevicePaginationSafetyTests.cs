
using System.Net;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisDevicePaginationSafetyTests
{
    private const string TenantId =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task RepeatedContinuationCursor_IsRejected(
        bool includeIntermediateCursor,
        int expectedResourceRequests)
    {
        var resourceRequests = 0;

        using var httpClient = AcronisDeviceTestSupport.CreateClient(
            request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path ==
                        "/bc/api/resource_manager/v1/epm/resources")
                    {
                        resourceRequests++;

                        var cursor =
                            resourceRequests switch
                            {
                                1 => "opaque-device-cursor-a",

                                2 when includeIntermediateCursor =>
                                    "opaque-device-cursor-b",

                                _ => "opaque-device-cursor-a"
                            };

                        return AcronisDeviceTestSupport.JsonResponse(
                            $$"""
                            {
                              "items": [],
                              "paging": {
                                "cursors": {
                                  "after": "{{cursor}}"
                                }
                              }
                            }
                            """
                        );
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                });

        var provider = AcronisDeviceTestSupport.CreateProvider(httpClient);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                });

        Assert.Contains(
            "repeated continuation cursor",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-device-cursor-a",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-device-cursor-b",
            exception.Message
        );

        Assert.Equal(
            expectedResourceRequests,
            resourceRequests
        );
    }

    [Fact]
    public async Task DistinctContinuationCursors_CompleteNormally()
    {
        var resourceRequests = 0;

        using var httpClient = AcronisDeviceTestSupport.CreateClient(
            request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path ==
                        "/bc/api/resource_manager/v1/epm/resources")
                    {
                        resourceRequests++;

                        if (resourceRequests == 1)
                        {
                            Assert.Contains(
                                "limit=30",
                                request.RequestUri!.Query
                            );

                            return ResourcePage(
                                "device-1",
                                "cursor-a"
                            );
                        }

                        if (resourceRequests == 2)
                        {
                            Assert.Contains(
                                "after=cursor-a",
                                request.RequestUri!.Query
                            );

                            return ResourcePage(
                                "device-2",
                                "cursor-b"
                            );
                        }

                        Assert.Contains(
                            "after=cursor-b",
                            request.RequestUri!.Query
                        );

                        return ResourcePage(
                            "device-3",
                            null
                        );
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                });

        var provider = AcronisDeviceTestSupport.CreateProvider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Equal(3, resourceRequests);
        Assert.Equal(3, resources.Count);

        Assert.Contains(resources, x => x.Id == "device-1");
        Assert.Contains(resources, x => x.Id == "device-2");
        Assert.Contains(resources, x => x.Id == "device-3");
    }

    private static HttpResponseMessage ResourcePage(
        string resourceId,
        string? after)
    {
        var paging =
            after is null
                ? """
                  "cursors": {}
                  """
                : $$"""
                  "cursors": {
                    "after": "{{after}}"
                  }
                  """;

        return AcronisDeviceTestSupport.JsonResponse(
            $$"""
            {
              "items": [
                {
                  "id": "{{resourceId}}",
                  "name": "Synthetic device",
                  "status": {
                    "state": "idle",
                    "lastSuccessBackup": "2026-10-08T12:00:00Z"
                  }
                }
              ],
              "paging": {
                {{paging}}
              }
            }
            """
        );
    }

}
