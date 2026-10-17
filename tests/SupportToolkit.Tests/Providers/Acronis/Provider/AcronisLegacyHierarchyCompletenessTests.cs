
using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisLegacyHierarchyCompletenessTests
{
    private const string RootUuid =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData(
        """
        {
          "id": 0,
          "uuid": "33333333-3333-3333-3333-333333333333",
          "has_children": 1
        }
        """)]
    [InlineData(
        """
        {
          "id": 2001,
          "uuid": null,
          "has_children": 1
        }
        """)]
    [InlineData(
        """
        {
          "id": 2001,
          "uuid": "not-a-valid-uuid",
          "has_children": 0
        }
        """)]
    [InlineData(
        """
        {
          "uuid": "33333333-3333-3333-3333-333333333333",
          "has_children": 1
        }
        """)]
    public async Task InvalidChild_FailsInsteadOfReturningPartialMapping(
        string invalidChildJson)
    {
        var rootLookupRequests = 0;
        var hierarchyRequests = 0;

        using var client =
            new HttpClient(
                new StubHttpMessageHandler(
                    request =>
                    {
                        var path =
                            request.RequestUri?.AbsolutePath
                            ?? string.Empty;

                        if (path == "/api/2/idp/token")
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

                        if (path == "/api/2/clients/fixture-client")
                        {
                            return JsonResponse(
                                $$"""
                                {
                                  "tenant_id": "{{RootUuid}}",
                                  "type": "api_client"
                                }
                                """
                            );
                        }

                        if (path == $"/api/1/groups/{RootUuid}")
                        {
                            rootLookupRequests++;

                            return JsonResponse(
                                $$"""
                                {
                                  "id": 9000,
                                  "uuid": "{{RootUuid}}"
                                }
                                """
                            );
                        }

                        if (path == "/api/1/groups/9000/children")
                        {
                            hierarchyRequests++;

                            /*
                             * Include one valid child before the invalid
                             * child to establish that partially collected
                             * mappings must not be returned or cached.
                             */
                            return JsonResponse(
                                $$"""
                                {
                                  "items": [
                                    {
                                      "id": 1001,
                                      "uuid": "11111111-1111-1111-1111-111111111111",
                                      "has_children": 0
                                    },
                                    {{invalidChildJson}}
                                  ]
                                }
                                """
                            );
                        }

                        return new HttpResponseMessage(
                            HttpStatusCode.NotFound
                        );
                    }
                )
            );

        var provider =
            new AcronisTenantProvider(
                new AcronisApiClient(
                    client,
                    new AcronisOptions
                    {
                        DatacenterUrl =
                            "https://fixture.acronis.invalid",
                        ClientId = "fixture-client",
                        ClientSecret = "fixture-secret"
                    }
                )
            );

        /*
         * Both attempts must fail.
         *
         * A failed traversal must never populate the provider's
         * successful mapping cache.
         */
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var exception =
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => provider.GetTenantIdMappingsAsync()
                );

            Assert.Contains(
                "Tenant ID mapping is incomplete",
                exception.Message
            );

            Assert.DoesNotContain(
                "not-a-valid-uuid",
                exception.Message
            );

            Assert.DoesNotContain(
                RootUuid,
                exception.Message
            );
        }

        Assert.Equal(2, rootLookupRequests);
        Assert.Equal(2, hierarchyRequests);
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
