
using System.Net;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisMicrosoft365PaginationSafetyTests
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
                                }
                              ]
                            }
                            """
                        );
                    }

                    if (path.StartsWith(
                            "/bc/api/resource_manager/v1/o365/groups/",
                            StringComparison.Ordinal))
                    {
                        resourceRequests++;

                        var cursor =
                            resourceRequests switch
                            {
                                1 => "opaque-cursor-a",

                                2 when includeIntermediateCursor =>
                                    "opaque-cursor-b",

                                _ => "opaque-cursor-a"
                            };

                        return AcronisProviderTestSupport.JsonResponse(
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
                }
            );

        var provider = AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                }
            );

        Assert.Contains(
            "repeated continuation cursor",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-cursor-a",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-cursor-b",
            exception.Message
        );

        Assert.Equal(
            expectedResourceRequests,
            resourceRequests
        );
    }

}
