using System.Text.Json;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
namespace SupportToolkit.Tests;

public class TenantExtensionDataTests
{
    [Fact]
    public void UnknownTenantFields_AreRetainedAsExtensionData()
    {
        const string json =
            """
            {
              "paging": {
                "cursors": {}
              },
              "timestamp": "2026-10-06T12:00:00Z",
              "items": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "name": "Customer Alpha",
                  "kind": "customer",
                  "undocumented_numeric_id": 123456,
                  "undocumented_string": "example",
                  "undocumented_flag": true
                }
              ]
            }
            """;

        var result =
            JsonSerializer.Deserialize<TenantPageDto>(
                json
            );

        Assert.NotNull(
            result
        );

        var tenant =
            Assert.Single(
                result.Items
            );

        Assert.Equal(
            3,
            tenant.AdditionalProperties.Count
        );

        Assert.Equal(
            JsonValueKind.Number,
            tenant.AdditionalProperties[
                "undocumented_numeric_id"
            ].ValueKind
        );

        Assert.Equal(
            JsonValueKind.String,
            tenant.AdditionalProperties[
                "undocumented_string"
            ].ValueKind
        );

        Assert.Equal(
            JsonValueKind.True,
            tenant.AdditionalProperties[
                "undocumented_flag"
            ].ValueKind
        );
    }
}
