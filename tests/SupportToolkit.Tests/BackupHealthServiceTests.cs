using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class BackupHealthServiceTests
{
    private static BackupHealthService CreateService()
    {
        var fixtureDirectory = Path.Combine(
            "Fixtures",
            "Acronis"
        );

        var provider = new FixtureAcronisProvider(
            fixtureDirectory
        );

        return new BackupHealthService(provider);
    }

    [Fact]
    public async Task GetBackupResourcesAsync_JoinsResourcesWithTenants()
    {
        var service = CreateService();

        var resources =
            await service.GetBackupResourcesAsync();

        Assert.Equal(3, resources.Count);

        var resource = resources.Single(
            resource =>
                resource.ResourceName == "BACKUP-SERVER-01"
        );

        Assert.Equal(
            "Customer Alpha",
            resource.TenantName
        );

        Assert.Equal(
            "idle",
            resource.Status
        );

        Assert.NotNull(
            resource.LastSuccessfulBackup
        );
    }

    [Fact]
    public async Task GetBackupResourcesAsync_MapsFailedResource()
    {
        var service = CreateService();

        var resources =
            await service.GetBackupResourcesAsync();

        var resource = resources.Single(
            resource =>
                resource.ResourceName == "FILES-01"
        );

        Assert.Equal(
            "Customer Beta",
            resource.TenantName
        );

        Assert.Equal(
            "error",
            resource.Status
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
            resource.LastSuccessfulBackup
        );
    }

    [Fact]
    public async Task GetBackupResourcesAsync_AttachesAlertsToMatchingResource()
    {
        var service = CreateService();

        var resources =
            await service.GetBackupResourcesAsync();

        var resource = resources.Single(
            resource =>
                resource.ResourceName == "FILES-01"
        );

        Assert.Single(resource.Alerts);

        var alert = resource.Alerts[0];

        Assert.Equal(
            "BackupFailed",
            alert.Type
        );

        Assert.Equal(
            "critical",
            alert.Severity
        );
    }

    [Fact]
    public async Task GetBackupResourcesAsync_ResourceWithoutAlertsHasEmptyAlertList()
    {
        var service = CreateService();

        var resources =
            await service.GetBackupResourcesAsync();

        var resource = resources.Single(
            resource =>
                resource.ResourceName == "OLD-PC-01"
        );

        Assert.Empty(resource.Alerts);
    }
}