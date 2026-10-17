
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisDeviceCollectionPresenceTests
{
    private const string TenantId =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"paging":{"cursors":{}}}""")]
    public async Task MissingItemsCollection_IsRejected(
        string resourceJson)
    {
        using var client = CreateClient(resourceJson);

        var provider =
            CreateProvider(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                }
            );

        Assert.Contains(
            "did not include an 'items' collection",
            exception.Message
        );
    }

    [Theory]
    [InlineData("""{"items":null}""")]
    [InlineData("""{"items":null,"paging":{"cursors":{}}}""")]
    public async Task ExplicitNullItemsCollection_IsRejectedClearly(
        string resourceJson)
    {
        using var client = CreateClient(resourceJson);

        var provider =
            CreateProvider(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                }
            );

        Assert.Contains(
            "null 'items' collection",
            exception.Message
        );
    }

    [Theory]
    [InlineData("""{"items":[],"paging":{"cursors":{}}}""")]
    [InlineData("""{"items":[]}""")]
    public async Task ExplicitlyEmptyItemsCollection_IsAccepted(
        string resourceJson)
    {
        using var client = CreateClient(resourceJson);

        var provider =
            CreateProvider(client);

        var resources =
            await provider.GetResourcesForTenantAsync(TenantId);

        Assert.Empty(resources);
    }

    [Fact]
    public void Dto_DistinguishesMissingAndExplicitNullCollections()
    {
        var missing =
            JsonSerializer.Deserialize<AcronisDeviceResourcePageDto>(
                "{}"
            );

        var explicitNull =
            JsonSerializer.Deserialize<AcronisDeviceResourcePageDto>(
                """{"items":null}"""
            );

        var empty =
            JsonSerializer.Deserialize<AcronisDeviceResourcePageDto>(
                """{"items":[]}"""
            );

        Assert.NotNull(missing);
        Assert.False(missing.HasItemsProperty);

        Assert.NotNull(explicitNull);
        Assert.True(explicitNull.HasItemsProperty);
        Assert.Null(explicitNull.Items);

        Assert.NotNull(empty);
        Assert.True(empty.HasItemsProperty);
        Assert.NotNull(empty.Items);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public async Task ValidDeviceStatus_IsStillDeserialized()
    {
        const string json =
            """
            {
              "items": [
                {
                  "id": "synthetic-device",
                  "name": "Synthetic server",
                  "type": "machine",
                  "status": {
                    "state": "notProtected",
                    "appliedPolicyNames": "Synthetic plan",
                    "lastBackup": "2026-10-08T12:00:00Z"
                  }
                }
              ],
              "paging": {
                "cursors": {}
              }
            }
            """;

        using var client = CreateClient(json);

        var provider =
            CreateProvider(client);

        var device =
            Assert.Single(
                await provider.GetResourcesForTenantAsync(TenantId)
            );

        Assert.Equal("synthetic-device", device.Id);
        Assert.Equal("notProtected", device.Status?.State);
        Assert.Equal(
            "Synthetic plan",
            device.Status?.AppliedPolicyNames
        );

        Assert.Equal(
            DateTimeOffset.Parse("2026-10-08T12:00:00Z"),
            device.Status?.LastBackup
        );
    }

    private static AcronisDeviceInventoryProvider CreateProvider(
        HttpClient client)
    {
        return new AcronisDeviceInventoryProvider(
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
    }

    private static HttpClient CreateClient(
        string resourceJson)
    {
        return new HttpClient(
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
                        return JsonResponse(resourceJson);
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
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
