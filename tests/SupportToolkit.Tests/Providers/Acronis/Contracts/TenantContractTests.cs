using System.Text.Json;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
namespace SupportToolkit.Tests;

public class TenantContractTests
{
    [Fact]
    public async Task TenantFixture_DeserializesSuccessfully()
    {
        var path = Path.Combine(
            "Fixtures",
            "Acronis",
            "tenants.json"
        );

        var json = await File.ReadAllTextAsync(path);

        var result = JsonSerializer.Deserialize<TenantPageDto>(json);

        Assert.NotNull(result);
        Assert.Equal(4, result.Items.Count);

        var partner = result.Items[0];
        var customer = result.Items[1];

        Assert.Equal("Example MSP", partner.Name);
        Assert.Equal("partner", partner.Kind);

        Assert.Equal("Customer Alpha", customer.Name);
        Assert.Equal("customer", customer.Kind);

        Assert.Equal(
            partner.Id,
            customer.ParentId
        );
    }
    [Fact]
    public void TenantWithoutOptionalAccountMetadata_DeserializesSuccessfully()
    {
        const string json =
            """
        {
          "paging": {
            "cursors": {}
          },
          "timestamp": "2026-10-01T12:00:00Z",
          "items": [
            {
              "id": "tenant-001",
              "name": "Customer Alpha"
            }
          ]
        }
        """;

        var result =
            JsonSerializer.Deserialize<TenantPageDto>(
                json
            );

        Assert.NotNull(result);

        var tenant =
            Assert.Single(result.Items);

        Assert.Equal(
            "Customer Alpha",
            tenant.Name
        );

        Assert.Null(
            tenant.ParentId
        );

        Assert.Null(
            tenant.Kind
        );

        Assert.Empty(
            tenant.Contacts
        );

        Assert.Empty(
            tenant.OfferingItems
        );
    }
}
