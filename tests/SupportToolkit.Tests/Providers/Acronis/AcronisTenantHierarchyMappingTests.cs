using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class AcronisTenantHierarchyMappingTests
{
    [Fact]
    public async Task TenantHierarchyTraversal_BootstrapsRootAndUsesNumericChildrenIds()
    {
        var legacyRequests =
            new List<string>();

        var handler =
            new StubHttpMessageHandler(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath;

                    if (path
                        == "/api/2/idp/token")
                    {
                        return JsonResponse(
                            """
                            {
                              "access_token": "fixture-token",
                              "token_type": "bearer",
                              "expires_on": 4102444800
                            }
                            """
                        );
                    }

                    if (path
                        == "/api/2/clients/fixture-client")
                    {
                        return JsonResponse(
                            """
                            {
                              "tenant_id":
                                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                              "type": "api_client"
                            }
                            """
                        );
                    }

                    if (path?.StartsWith(
                            "/api/1/groups/",
                            StringComparison.Ordinal)
                        == true)
                    {
                        legacyRequests.Add(
                            path
                        );
                    }

                    /*
                     * One-time UUID -> numeric bootstrap.
                     */
                    if (path
                        == "/api/1/groups/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                    {
                        return JsonResponse(
                            """
                            {
                              "id": 9000,
                              "uuid":
                                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                            }
                            """
                        );
                    }

                    /*
                     * Root hierarchy lookup uses the numeric ID obtained
                     * above, not the UUID.
                     */
                    if (path
                        == "/api/1/groups/9000/children")
                    {
                        return JsonResponse(
                            """
                            {
                              "items": [
                                {
                                  "id": 1001,
                                  "uuid":
                                    "11111111-1111-1111-1111-111111111111",
                                  "has_children": 1
                                },
                                {
                                  "id": 1002,
                                  "uuid":
                                    "22222222-2222-2222-2222-222222222222",
                                  "has_children": 0
                                }
                              ]
                            }
                            """
                        );
                    }

                    /*
                     * Only the branch node gets another request.
                     */
                    if (path
                        == "/api/1/groups/1001/children")
                    {
                        return JsonResponse(
                            """
                            {
                              "items": [
                                {
                                  "id": 2001,
                                  "uuid":
                                    "33333333-3333-3333-3333-333333333333",
                                  "has_children": 0
                                }
                              ]
                            }
                            """
                        );
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

        using var httpClient =
            new HttpClient(
                handler
            );

        var options =
            new AcronisOptions
            {
                DatacenterUrl =
                    "https://fixture.acronis.invalid",

                ClientId =
                    "fixture-client",

                ClientSecret =
                    "fixture-secret"
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options
            );

        var provider =
            new HttpAcronisProvider(
                apiClient
            );

        var mappings =
            await provider
                .GetTenantIdMappingsAsync();

        /*
         * Root + two direct children + one nested child.
         */
        Assert.Equal(
            4,
            mappings.Count
        );

        Assert.Equal(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            mappings["9000"]
        );

        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            mappings["1001"]
        );

        Assert.Equal(
            "22222222-2222-2222-2222-222222222222",
            mappings["1002"]
        );

        Assert.Equal(
            "33333333-3333-3333-3333-333333333333",
            mappings["2001"]
        );

        Assert.Equal(
            [
                "/api/1/groups/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "/api/1/groups/9000/children",
                "/api/1/groups/1001/children"
            ],
            legacyRequests
        );

        /*
         * Crucially, traversal never tries UUID/children.
         */
        Assert.DoesNotContain(
            legacyRequests,
            path =>
                path.Contains(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/children",
                    StringComparison.Ordinal
                )
        );
    }

    [Fact]
    public async Task TenantHierarchyTraversal_IsCachedForProviderLifetime()
    {
        var hierarchyRequestCount =
            0;

        var handler =
            new StubHttpMessageHandler(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath;

                    if (path
                        == "/api/2/idp/token")
                    {
                        return JsonResponse(
                            """
                            {
                              "access_token": "fixture-token",
                              "token_type": "bearer",
                              "expires_on": 4102444800
                            }
                            """
                        );
                    }

                    if (path
                        == "/api/2/clients/fixture-client")
                    {
                        return JsonResponse(
                            """
                            {
                              "tenant_id":
                                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                              "type": "api_client"
                            }
                            """
                        );
                    }

                    if (path
                        == "/api/1/groups/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
                    {
                        hierarchyRequestCount++;

                        return JsonResponse(
                            """
                            {
                              "id": 9000,
                              "uuid":
                                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                            }
                            """
                        );
                    }

                    if (path
                        == "/api/1/groups/9000/children")
                    {
                        hierarchyRequestCount++;

                        return JsonResponse(
                            """
                            {
                              "items": []
                            }
                            """
                        );
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

        using var httpClient =
            new HttpClient(
                handler
            );

        var options =
            new AcronisOptions
            {
                DatacenterUrl =
                    "https://fixture.acronis.invalid",

                ClientId =
                    "fixture-client",

                ClientSecret =
                    "fixture-secret"
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options
            );

        var provider =
            new HttpAcronisProvider(
                apiClient
            );

        var first =
            await provider
                .GetTenantIdMappingsAsync();

        var second =
            await provider
                .GetTenantIdMappingsAsync();

        Assert.Same(
            first,
            second
        );

        Assert.Equal(
            2,
            hierarchyRequestCount
        );
    }

    private static HttpResponseMessage JsonResponse(
        string json)
    {
        return new HttpResponseMessage(
            HttpStatusCode.OK
        )
        {
            Content =
                new StringContent(
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
            HttpResponseMessage> _handler;

        public StubHttpMessageHandler(
            Func<
                HttpRequestMessage,
                HttpResponseMessage> handler)
        {
            _handler =
                handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _handler(request)
            );
        }
    }
}