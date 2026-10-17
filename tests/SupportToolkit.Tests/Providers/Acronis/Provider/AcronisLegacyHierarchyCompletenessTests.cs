
using System.Net;

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
            AcronisProviderTestSupport.CreateClient(
                    request =>
                    {
                        var path =
                            request.RequestUri?.AbsolutePath
                            ?? string.Empty;

                        if (path == "/api/2/clients/fixture-client")
                        {
                            return AcronisProviderTestSupport.JsonResponse(
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

                            return AcronisProviderTestSupport.JsonResponse(
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
                            return AcronisProviderTestSupport.JsonResponse(
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
            );

        var provider = AcronisProviderTestSupport.CreateTenantProvider(client);

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

}
