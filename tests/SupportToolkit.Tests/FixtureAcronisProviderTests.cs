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

        Assert.Equal(3, resources.Count);

        Assert.Equal(
            "BACKUP-SERVER-01",
            resources[0].Context.Name
        );

        Assert.Equal(
            "error",
            resources[1].Aggregate?.Status
        );

        Assert.Equal(
            "no_policies_applied",
            resources[2].Aggregate?.Status
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
            alert => alert.Severity == "critical"
        );
    }
}