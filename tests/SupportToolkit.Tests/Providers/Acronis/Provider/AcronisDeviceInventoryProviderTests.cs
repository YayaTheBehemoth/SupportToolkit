using System.Net;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisDeviceInventoryProviderTests
{
    [Fact]
    public async Task GetResourcesForTenantAsync_UsesCanonicalAllDevicesGroupAndDeduplicates()
    {
        var resourceRequests = 0;

        using var httpClient = AcronisDeviceTestSupport.CreateClient(
            request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path == "/bc/api/resource_manager/v1/epm/resources")
                    {
                        resourceRequests++;

                        if (resourceRequests == 1)
                        {
                            Assert.Contains(
                                "parentId=f656db8d-3b82-40ba-b06a-bffc9b0b825a",
                                request.RequestUri!.Query
                            );

                            return AcronisDeviceTestSupport.JsonResponse(
                                """
                                {
                                  "items": [
                                    {
                                      "id": "device-1",
                                      "name": "SERVER-01",
                                      "type": "machine",
                                      "status": {
                                        "state": "idle",
                                        "lastBackup": "2026-10-08T12:00:00Z",
                                        "lastSuccessBackup": "2026-10-08T12:00:00Z"
                                      }
                                    }
                                  ],
                                  "paging": {
                                    "cursors": {
                                      "after": "next page=="
                                    }
                                  }
                                }
                                """
                            );
                        }

                        Assert.Contains(
                            "after=next%20page%3D%3D",
                            request.RequestUri!.Query
                        );

                        return AcronisDeviceTestSupport.JsonResponse(
                            """
                            {
                              "items": [
                                {
                                  "id": "device-1",
                                  "name": "SERVER-01",
                                  "type": "machine",
                                  "status": {
                                    "state": "idle",
                                    "lastBackup": "2026-10-08T12:00:00Z",
                                    "lastSuccessBackup": "2026-10-08T12:00:00Z"
                                  }
                                },
                                {
                                  "id": "device-2",
                                  "name": "SQL-SRV",
                                  "type": "machine",
                                  "status": {
                                    "state": "notProtected",
                                    "appliedPolicyNames": "SQL Database Backup (Disabled)"
                                  }
                                }
                              ],
                              "paging": {
                                "cursors": {}
                              }
                            }
                            """
                        );
                    }

                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                });

        var provider = AcronisDeviceTestSupport.CreateProvider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            );

        Assert.Equal(
            2,
            resourceRequests
        );
        Assert.Equal(
            2,
            resources.Count
        );
        Assert.Contains(
            resources,
            resource =>
                resource.Name == "SQL-SRV"
                && resource.Status?.State == "notProtected"
        );
    }

}
