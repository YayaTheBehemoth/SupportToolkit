
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Transport;

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

                        return JsonResponse(
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

        using var httpClient =
            new HttpClient(handler);

        var provider =
            CreateProvider(httpClient);

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
                }
            );

        using var httpClient =
            new HttpClient(handler);

        var provider =
            CreateProvider(httpClient);

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

        return JsonResponse(
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

    private static AcronisDeviceInventoryProvider CreateProvider(
        HttpClient httpClient)
    {
        return new AcronisDeviceInventoryProvider(
            new AcronisApiClient(
                httpClient,
                new AcronisOptions
                {
                    DatacenterUrl =
                        "https://fixture.acronis.invalid",
                    ClientId = "fixture-client",
                    ClientSecret = "fixture-secret"
                }
            )
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
