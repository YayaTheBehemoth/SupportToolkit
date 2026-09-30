using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class BackupHealthServiceTests
{
    private static BackupHealthService CreateService()
    {
        var resourceStatusesPath = Path.Combine(
            "Fixtures",
            "Acronis",
            "resource-statuses.json"
        );

        var tenantsPath = Path.Combine(
            "Fixtures",
            "Acronis",
            "tenants.json"
        );

        var provider = new FixtureAcronisProvider(
            resourceStatusesPath,
            tenantsPath
        );

        return new BackupHealthService(provider);
    }

    [Fact]
    public async Task GetBackupResourcesAsync_JoinsResourcesWithTenants()
    {
        var service = CreateService();

        var resources = await service.GetBackupResourcesAsync();

        Assert.Equal(3, resources.Count);

        var healthyResource = resources.Single(
            resource =>
                resource.ResourceName == "BACKUP-SERVER-01"
        );

        Assert.Equal(
            "Customer Alpha",
            healthyResource.TenantName
        );

        Assert.Equal(
            "idle",
            healthyResource.Status
        );

        Assert.NotNull(
            healthyResource.LastSuccessfulBackup
        );
    }

    [Fact]
    public async Task GetBackupResourcesAsync_MapsFailedResource()
    {
        var service = CreateService();

        var resources = await service.GetBackupResourcesAsync();

        var failedResource = resources.Single(
            resource =>
                resource.ResourceName == "FILES-01"
        );

        Assert.Equal(
            "Customer Beta",
            failedResource.TenantName
        );

        Assert.Equal(
            "error",
            failedResource.Status
        );

        Assert.Equal(
            new DateTimeOffset(
                2026,
                9,
                27,
                3,
                0,
                0,
                TimeSpan.Zero
            ),
            failedResource.LastSuccessfulBackup
        );
    }
}