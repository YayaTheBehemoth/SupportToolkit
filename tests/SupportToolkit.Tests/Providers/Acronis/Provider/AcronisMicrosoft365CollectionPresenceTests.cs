
using System.Net;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisMicrosoft365CollectionPresenceTests
{
    private const string TenantId =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    private const string ValidLeafGroups =
        """
        {
          "data": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "leaf": true
            }
          ]
        }
        """;

    [Theory]
    [InlineData("{}")]
    public async Task MissingGroupCollection_IsRejected(
        string groupsJson)
    {
        using var httpClient =
            CreateHttpClient(groupsJson);

        var provider =
            AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                }
            );

        Assert.Contains(
            "'data' or 'items'",
            exception.Message
        );
    }

    [Theory]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"items":null}""")]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"items":[]}""")]
    public async Task ExplicitlyEmptyGroups_AreAccepted(
        string groupsJson)
    {
        using var httpClient =
            CreateHttpClient(groupsJson);

        var provider =
            AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Empty(resources);
    }

    [Theory]
    [InlineData("{}")]
    public async Task MissingResourceCollection_IsRejected(
        string resourcesJson)
    {
        using var httpClient =
            CreateHttpClient(
                ValidLeafGroups,
                resourcesJson
            );

        var provider =
            AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                }
            );

        Assert.Contains(
            "'items' collection",
            exception.Message
        );
    }

    [Theory]
    [InlineData("""{"items":null,"paging":{"cursors":{}}}""")]
    [InlineData("""{"items":[],"paging":{"cursors":{}}}""")]
    public async Task ExplicitlyEmptyResourceCollection_IsAccepted(
        string resourcesJson)
    {
        using var httpClient =
            CreateHttpClient(
                ValidLeafGroups,
                resourcesJson
            );

        var provider =
            AcronisProviderTestSupport.CreateMicrosoft365Provider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Empty(resources);
    }

    private static HttpClient CreateHttpClient(
        string groupsJson,
        string? resourcesJson = null)
    {
        resourcesJson ??=
            """
            {
              "items": [],
              "paging": {
                "cursors": {}
              }
            }
            """;

        return AcronisProviderTestSupport.CreateClient(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath
                        ?? string.Empty;

                    if (path == "/bc/api/resource_manager/v1/o365/groups")
                    {
                        return AcronisProviderTestSupport.JsonResponse(groupsJson);
                    }

                    if (path.StartsWith(
                            "/bc/api/resource_manager/v1/o365/groups/",
                            StringComparison.Ordinal))
                    {
                        return AcronisProviderTestSupport.JsonResponse(resourcesJson!);
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

    }

}
