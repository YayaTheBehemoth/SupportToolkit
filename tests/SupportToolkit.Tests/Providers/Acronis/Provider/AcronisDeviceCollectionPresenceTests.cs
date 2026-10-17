
using System.Net;
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Devices.Dtos;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisDeviceCollectionPresenceTests
{
    private const string TenantId =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Theory]
    [InlineData("{}")]
    public async Task MissingItemsCollection_IsRejected(
        string resourceJson)
    {
        using var client = CreateClient(resourceJson);

        var provider =
            AcronisDeviceTestSupport.CreateProvider(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                });

        Assert.Contains(
            "did not include an 'items' collection",
            exception.Message
        );
    }

    [Theory]
    [InlineData("""{"items":null}""")]
    public async Task ExplicitNullItemsCollection_IsRejectedClearly(
        string resourceJson)
    {
        using var client = CreateClient(resourceJson);

        var provider =
            AcronisDeviceTestSupport.CreateProvider(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    await provider.GetResourcesForTenantAsync(TenantId);
                });

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
            AcronisDeviceTestSupport.CreateProvider(client);

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
            AcronisDeviceTestSupport.CreateProvider(client);

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

    private static HttpClient CreateClient(string resourceJson)
    {
        return AcronisDeviceTestSupport.CreateClient(
            request => request.RequestUri?.AbsolutePath ==
                "/bc/api/resource_manager/v1/epm/resources"
                ? AcronisDeviceTestSupport.JsonResponse(resourceJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
