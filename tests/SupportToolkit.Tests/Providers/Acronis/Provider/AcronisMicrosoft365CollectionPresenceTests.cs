
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Transport;

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
    [InlineData("""{"paging":{"cursors":{}}}""")]
    public async Task MissingGroupCollection_IsRejected(
        string groupsJson)
    {
        using var httpClient =
            CreateHttpClient(groupsJson);

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
            CreateProvider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Empty(resources);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"paging":{"cursors":{}}}""")]
    public async Task MissingResourceCollection_IsRejected(
        string resourcesJson)
    {
        using var httpClient =
            CreateHttpClient(
                ValidLeafGroups,
                resourcesJson
            );

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
            CreateProvider(httpClient);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Empty(resources);
    }

    private static AcronisMicrosoft365InventoryProvider CreateProvider(
        HttpClient httpClient)
    {
        return new AcronisMicrosoft365InventoryProvider(
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
                        return JsonResponse(groupsJson);
                    }

                    if (path.StartsWith(
                            "/bc/api/resource_manager/v1/o365/groups/",
                            StringComparison.Ordinal))
                    {
                        return JsonResponse(resourcesJson!);
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            );

        return new HttpClient(handler);
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
            Content = new StringContent(
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
            return Task.FromResult(_handler(request));
        }
    }
}
