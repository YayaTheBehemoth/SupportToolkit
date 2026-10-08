using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisMicrosoft365InventoryProviderTests
{
    [Fact]
    public async Task GetResourcesForTenantAsync_UsesLeafGroupsFollowsPaginationAndDeduplicates()
    {
        var resourceRequests = 0;
        var nonLeafGroupRequested = false;

        var handler =
            new StubHttpMessageHandler(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path == "/api/2/idp/token")
                    {
                        return TokenResponse(
                            request.Headers.Authorization
                        );
                    }

                    if (path == "/bc/api/resource_manager/v1/o365/groups")
                    {
                        return JsonResponse(
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

                        return NotFound();
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

                            return JsonResponse(
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

                        return JsonResponse(
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

                    return NotFound();
                }
            );

        using var httpClient =
            new HttpClient(
                handler
            );

        var provider =
            new AcronisMicrosoft365InventoryProvider(
                CreateApiClient(httpClient)
            );

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

    private static AcronisApiClient CreateApiClient(
        HttpClient httpClient)
    {
        return new AcronisApiClient(
            httpClient,
            new AcronisOptions
            {
                DatacenterUrl =
                    "https://fixture.acronis.invalid",
                ClientId =
                    "fixture-client",
                ClientSecret =
                    "fixture-secret"
            }
        );
    }

    private static HttpResponseMessage TokenResponse(
        AuthenticationHeaderValue? authorization)
    {
        var token =
            string.Equals(
                authorization?.Scheme,
                "Basic",
                StringComparison.OrdinalIgnoreCase
            )
                ? "base-token"
                : "scoped-token";

        return JsonResponse(
            $$"""
            {
              "access_token": "{{token}}",
              "token_type": "bearer",
              "expires_on": 4102444800
            }
            """
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

    private static HttpResponseMessage NotFound()
    {
        return new HttpResponseMessage(
            HttpStatusCode.NotFound
        );
    }

    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>
            _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
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
