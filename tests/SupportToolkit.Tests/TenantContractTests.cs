using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

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
}