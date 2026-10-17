using System.Net;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisMicrosoft365InventoryProviderTests
{
    [Fact]
    public async Task GetResourcesForTenantAsync_UsesLeafGroupsFollowsPaginationAndDeduplicates()
    {
        var resourceRequests = 0;
        var nonLeafGroupRequested = false;

        using var httpClient = AcronisProviderTestSupport.CreateClient(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path == "/bc/api/resource_manager/v1/o365/groups")
                    {
                        return AcronisProviderTestSupport.JsonResponse(
                            """
                            {
                              "data": [
                                {
                                  "id": "11111111-1111-1111-1111-111111111111",
                                  "leaf": true
                                },
                                {
                                  "id": "22222222-2222-2222-2222-222222222222",
                                  "leaf": false
                                }
                              ]
                            }
                            """
                        );
                    }

                    if (path.Contains(
                            "22222222-2222-2222-2222-222222222222",
                            StringComparison.Ordinal
                        ))
                    {
                        nonLeafGroupRequested = true;

                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    }

                    if (path.Contains(
                            "11111111-1111-1111-1111-111111111111",
                            StringComparison.Ordinal
                        ))
                    {
                        resourceRequests++;

                        if (resourceRequests == 1)
                        {
                            Assert.Contains(
                                "limit=30",
                                request.RequestUri!.Query
                            );

                            return AcronisProviderTestSupport.JsonResponse(
                                """
                                {
                                  "items": [
                                    {
                                      "id": "resource-1",
                                      "name": "Mailbox",
                                      "hasProtections": true,
                                      "lastTaskStatus": "ok",
                                      "lastTaskState": "idle",
                                      "lastSuccessTime": "2026-10-08T12:00:00Z"
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

                        return AcronisProviderTestSupport.JsonResponse(
                            """
                            {
                              "items": [
                                {
                                  "id": "resource-1",
                                  "name": "Mailbox",
                                  "hasProtections": true,
                                  "lastTaskStatus": "ok",
                                  "lastTaskState": "idle",
                                  "lastSuccessTime": "2026-10-08T12:00:00Z"
                                },
                                {
                                  "id": "resource-2",
                                  "name": "OneDrive",
                                  "hasProtections": true,
                                  "lastTaskStatus": "ok",
                                  "lastTaskState": "idle",
                                  "lastSuccessTime": "2026-10-08T12:00:00Z"
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
                }
            );

        var provider = AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            );

        Assert.False(
            nonLeafGroupRequested
        );
        Assert.Equal(
            2,
            resourceRequests
        );
        Assert.Equal(
            2,
            resources.Count
        );
    }

}
