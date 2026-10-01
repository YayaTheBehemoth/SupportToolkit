using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class FixtureAcronisProviderTests
{
    private static FixtureAcronisProvider CreateProvider()
    {
        var fixtureDirectory = Path.Combine(
            "Fixtures",
            "Acronis"
        );

        return new FixtureAcronisProvider(
            fixtureDirectory
        );
    }

    [Fact]
    public async Task GetResourceStatusesAsync_ReturnsFixtureResources()
    {
        var provider = CreateProvider();

        var resources =
            await provider.GetResourceStatusesAsync();

        Assert.Equal(4, resources.Count);

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "BACKUP-SERVER-01"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "FILES-01"
                && resource.Aggregate?.Status == "error"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "OLD-PC-01"
                && resource.Aggregate?.Status
                    == "no_policies_applied"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name == "APP-SERVER-01"
                && resource.Aggregate?.Status == "idle"
        );
    }

    [Fact]
    public async Task GetTenantsAsync_ReturnsFixtureTenants()
    {
        var provider = CreateProvider();

        var tenants =
            await provider.GetTenantsAsync();

        Assert.Equal(4, tenants.Count);

        Assert.Contains(
            tenants,
            tenant => tenant.Name == "Customer Alpha"
        );

        Assert.Contains(
            tenants,
            tenant => tenant.Name == "Customer Beta"
        );

        Assert.Contains(
            tenants,
            tenant => tenant.Name == "Customer Gamma"
        );
    }

    [Fact]
    public async Task GetAlertsAsync_ReturnsFixtureAlerts()
    {
        var provider = CreateProvider();

        var alerts =
            await provider.GetAlertsAsync();

        Assert.Equal(2, alerts.Count);

        Assert.Contains(
            alerts,
            alert => alert.Type == "BackupFailed"
        );

        Assert.Contains(
            alerts,
            alert => alert.Type == "NoBackupForXDays"
        );
    }
}