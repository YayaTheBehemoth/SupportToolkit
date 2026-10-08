using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisDeviceInventoryProviderTests
{
    [Fact]
    public async Task GetResourcesForTenantAsync_UsesCanonicalAllDevicesGroupAndDeduplicates()
    {
        var resourceRequests = 0;

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

                    if (path == "/bc/api/resource_manager/v1/epm/resources")
                    {
                        resourceRequests++;

                        if (resourceRequests == 1)
                        {
                            Assert.Contains(
                                "parentId=f656db8d-3b82-40ba-b06a-bffc9b0b825a",
                                request.RequestUri!.Query
                            );

                            return JsonResponse(
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

                        return JsonResponse(
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

                    return NotFound();
                }
            );

        using var httpClient =
            new HttpClient(
                handler
            );

        var provider =
            new AcronisDeviceInventoryProvider(
                CreateApiClient(httpClient)
            );

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
