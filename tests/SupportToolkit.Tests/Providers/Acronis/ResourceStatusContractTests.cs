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
        Assert.Equal(4, result.Items.Count);

        var backupServer = result.Items.Single(
            resource =>
                resource.Context.Name == "BACKUP-SERVER-01"
        );

        Assert.Equal(
            "11111111-aaaa-aaaa-aaaa-111111111111",
            backupServer.Context.TenantId
        );

        Assert.Equal(
            "idle",
            backupServer.Aggregate?.Status
        );

        Assert.Equal(
            "policy.backup.machine",
            backupServer.Policies?[0].Type
        );

        var healthyServer = result.Items.Single(
            resource =>
                resource.Context.Name == "APP-SERVER-01"
        );

        Assert.Equal(
            "idle",
            healthyServer.Aggregate?.Status
        );

        Assert.Equal(
            new DateTimeOffset(
                2026,
                10,
                1,
                4,
                0,
                0,
                TimeSpan.Zero
            ),
            healthyServer.Policies?[0].LastSuccessRunTime
        );
    }
}