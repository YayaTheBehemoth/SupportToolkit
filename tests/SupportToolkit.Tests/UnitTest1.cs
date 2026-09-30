using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Tests;

public class ResourceStatusContractTests
{
    [Fact]
    public async Task ResourceStatusFixture_DeserializesSuccessfully()
    {
        var path = Path.Combine(
            "Fixtures",
            "Acronis",
            "resource-statuses.json"
        );

        var json = await File.ReadAllTextAsync(path);

        var result =
            JsonSerializer.Deserialize<ResourceStatusPageDto>(json);

        Assert.NotNull(result);
        Assert.Equal(3, result.Items.Count);

        var first = result.Items[0];

        Assert.Equal("BACKUP-SERVER-01", first.Context.Name);
        Assert.Equal("11111111-aaaa-aaaa-aaaa-111111111111", first.Context.TenantId);
        Assert.Equal("idle", first.Aggregate?.Status);
        Assert.Equal(
            "policy.backup.machine",
            first.Policies?[0].Type
        );
    }
}