
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Transport;

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
            new AcronisMicrosoft365InventoryProvider(
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
        return new HttpResponseMessage(HttpStatusCode.OK)
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
