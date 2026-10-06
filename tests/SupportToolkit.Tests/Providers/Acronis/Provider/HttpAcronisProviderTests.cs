using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests.Providers.Acronis;

public class HttpAcronisProviderTests
{
    [Fact]
    public async Task GetResourceStatusesAsync_FollowsCursorAndAggregatesPages()
    {
        var resourcePageRequests = 0;

        var handler = new StubHttpMessageHandler(
            request =>
            {
                if (request.RequestUri?.AbsolutePath
                    == "/api/2/idp/token")
                {
                    return TokenResponse();
                }

                if (request.RequestUri?.AbsolutePath
                    == "/api/resource_management/v4/resource_statuses")
                {
                    resourcePageRequests++;

                    if (resourcePageRequests == 1)
                    {
                        Assert.Contains(
                            "limit=100",
                            request.RequestUri.Query
                        );

                        Assert.DoesNotContain(
                            "after=",
                            request.RequestUri.Query
                        );

                        return JsonResponse(
                            """
                            {
                              "paging": {
                                "cursors": {
                                  "after": "next page=="
                                }
                              },
                              "items": [
                                {
                                  "context": {
                                    "id": "resource-001",
                                    "name": "SERVER-01",
                                    "tenant_id": "tenant-001",
                                    "type": "resource.machine",
                                    "cti": "fixture.machine",
                                    "parent_group_ids": []
                                  }
                                }
                              ],
                              "timestamp": "2026-10-01T12:00:00Z"
                            }
                            """
                        );
                    }

                    Assert.Contains(
                        "limit=100",
                        request.RequestUri.Query
                    );

                    Assert.Contains(
                        "after=next%20page%3D%3D",
                        request.RequestUri.Query
                    );

                    return JsonResponse(
                        """
                        {
                          "paging": {
                            "cursors": {}
                          },
                          "items": [
                            {
                              "context": {
                                "id": "resource-002",
                                "name": "SERVER-02",
                                "tenant_id": "tenant-001",
                                "type": "resource.machine",
                                "cti": "fixture.machine",
                                "parent_group_ids": []
                              }
                            }
                          ],
                          "timestamp": "2026-10-01T12:00:00Z"
                        }
                        """
                    );
                }

                return NotFound();
            }
        );

        using var httpClient =
            new HttpClient(handler);

        var apiClient =
            CreateApiClient(httpClient);

        var provider =
            new HttpAcronisProvider(apiClient);

        var resources =
            await provider.GetResourceStatusesAsync();

        Assert.Equal(
            2,
            resourcePageRequests
        );

        Assert.Equal(
            2,
            resources.Count
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "SERVER-01"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "SERVER-02"
        );
    }

    [Fact]
    public async Task GetTenantsAsync_UsesRootTenantAndFetchesSubtree()
    {
        var handler = new StubHttpMessageHandler(
            request =>
            {
                if (request.RequestUri?.AbsolutePath
                    == "/api/2/idp/token")
                {
                    return TokenResponse();
                }

                if (request.RequestUri?.AbsolutePath
                    == "/api/2/clients/fixture-client")
                {
                    return JsonResponse(
                        """
                        {
                          "tenant_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                          "type": "api_client"
                        }
                        """
                    );
                }

                if (request.RequestUri?.AbsolutePath
                    == "/api/2/tenants")
                {
                    Assert.Contains(
                        "subtree_root_id=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        request.RequestUri.Query
                    );

                    Assert.Contains(
                        "lod=basic",
                        request.RequestUri.Query
                    );

                    Assert.Contains(
                        "limit=100",
                        request.RequestUri.Query
                    );

                    return JsonResponse(
                        """
                        {
                          "paging": {
                            "cursors": {}
                          },
                          "timestamp": "2026-10-01T12:00:00Z",
                          "items": [
                            {
                              "id": "11111111-aaaa-aaaa-aaaa-111111111111",
                              "parent_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                              "version": 1,
                              "created_at": "2026-01-01T00:00:00Z",
                              "updated_at": "2026-10-01T00:00:00Z",
                              "deleted_at": null,
                              "name": "Customer Alpha",
                              "kind": "customer",
                              "enabled": true,
                              "contacts": [],
                              "offering_items": []
                            }
                          ]
                        }
                        """
                    );
                }

                return NotFound();
            }
        );

        using var httpClient =
            new HttpClient(handler);

        var apiClient =
            CreateApiClient(httpClient);

        var provider =
            new HttpAcronisProvider(apiClient);

        var tenants =
            await provider.GetTenantsAsync();

        var tenant =
            Assert.Single(tenants);

        Assert.Equal(
            "Customer Alpha",
            tenant.Name
        );

        Assert.Equal(
            "customer",
            tenant.Kind
        );
    }

    [Fact]
    public async Task GetAlertsAsync_FetchesActiveAlerts()
    {
        var handler = new StubHttpMessageHandler(
            request =>
            {
                if (request.RequestUri?.AbsolutePath
                    == "/api/2/idp/token")
                {
                    return TokenResponse();
                }

                if (request.RequestUri?.AbsolutePath
                    == "/api/alert_manager/v1/alerts")
                {
                    Assert.Contains(
                        "show_deleted=false",
                        request.RequestUri.Query
                    );

                    Assert.Contains(
                        "limit=100",
                        request.RequestUri.Query
                    );

                    return JsonResponse(
                        """
                        {
                          "items": [
                            {
                              "id": "alert-001",
                              "createdAt": "2026-10-01T10:00:00Z",
                              "details": {
                                "resourceId": "resource-001",
                                "resourceName": "SERVER-01"
                              },
                              "category": "Backup",
                              "severity": "critical",
                              "type": "BackupFailed"
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
            new HttpClient(handler);

        var apiClient =
            CreateApiClient(httpClient);

        var provider =
            new HttpAcronisProvider(apiClient);

        var alerts =
            await provider.GetAlertsAsync();

        var alert =
            Assert.Single(alerts);

        Assert.Equal(
            "BackupFailed",
            alert.Type
        );

        Assert.Equal(
            "critical",
            alert.Severity
        );
    }

    private static AcronisApiClient CreateApiClient(
        HttpClient httpClient)
    {
        var options = new AcronisOptions
        {
            DatacenterUrl =
                "https://fixture.acronis.invalid",

            ClientId =
                "fixture-client",

            ClientSecret =
                "fixture-secret"
        };

        return new AcronisApiClient(
            httpClient,
            options
        );
    }

    private static HttpResponseMessage TokenResponse()
    {
        // Far-future expiry keeps authentication deterministic
        // during tests without depending on the system clock.
        return JsonResponse(
            """
            {
              "access_token": "fixture-access-token",
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
            Content = new StringContent(
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

    /// <summary>
    /// Replaces the real network boundary so provider HTTP behaviour can
    /// be tested without Acronis credentials or external requests.
    /// </summary>
    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            HttpResponseMessage
        > _handler;

        public StubHttpMessageHandler(
            Func<
                HttpRequestMessage,
                HttpResponseMessage
            > handler)
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