using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class FixtureAcronisProviderTests
{
    private static FixtureAcronisProvider CreateProvider()
    {
        var fixtureDirectory =
            Path.Combine(
                "Fixtures",
                "Acronis"
            );

        return new FixtureAcronisProvider(
            fixtureDirectory
        );
    }

    [Fact]
    public async Task GetResourceStatusesAsync_ReturnsProductionShapedFixtureResources()
    {
        var provider =
            CreateProvider();

        var resources =
            await provider
                .GetResourceStatusesAsync();

        Assert.Equal(
            7,
            resources.Count
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name
                    == "BACKUP-SERVER-01"
                && resource.Context.TenantId
                    == "1001"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name
                    == "FILES-01"
                && resource.Aggregate?.Status
                    == "error"
                && resource.Context.TenantId
                    == "1002"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name
                    == "OLD-PC-01"
                && resource.Aggregate?.Status
                    == "no_policies_applied"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Name
                    == "APP-SERVER-01"
                && resource.Aggregate?.Status
                    == "idle"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Type
                    == "resource.group.all"
                && resource.Context.TenantId
                    == "0"
        );

        Assert.Contains(
            resources,
            resource =>
                resource.Context.Type.StartsWith(
                    "resource.group.",
                    StringComparison.Ordinal
                )
                && resource.Aggregate?.Status
                    == "critical"
        );
    }

    [Fact]
    public async Task GetTenantsAsync_ReturnsFixtureTenants()
    {
        var provider =
            CreateProvider();

        var tenants =
            await provider
                .GetTenantsAsync();

        Assert.Equal(
            4,
            tenants.Count
        );

        Assert.Contains(
            tenants,
            tenant =>
                tenant.Name
                    == "Customer Alpha"
        );

        Assert.Contains(
            tenants,
            tenant =>
                tenant.Name
                    == "Customer Beta"
        );

        Assert.Contains(
            tenants,
            tenant =>
                tenant.Name
                    == "Customer Gamma"
        );

        Assert.All(
            tenants,
            tenant =>
                Assert.Null(
                    tenant.CustomerId
                )
        );
    }

    [Fact]
    public async Task GetAlertsAsync_ReturnsFixtureAlerts()
    {
        var provider =
            CreateProvider();

        var alerts =
            await provider
                .GetAlertsAsync();

        Assert.Equal(
            2,
            alerts.Count
        );

        Assert.Contains(
            alerts,
            alert =>
                alert.Type
                    == "BackupFailed"
        );

        Assert.Contains(
            alerts,
            alert =>
                alert.Type
                    == "NoBackupForXDays"
        );
    }

    [Fact]
    public async Task GetTenantIdMappingsAsync_ReturnsLegacyToUuidMappings()
    {
        var provider =
            CreateProvider();

        var mappings =
            await provider
                .GetTenantIdMappingsAsync();

        Assert.Equal(
            4,
            mappings.Count
        );

        Assert.Equal(
            "11111111-aaaa-aaaa-aaaa-111111111111",
            mappings["1001"]
        );

        Assert.Equal(
            "22222222-bbbb-bbbb-bbbb-222222222222",
            mappings["1002"]
        );

        Assert.Equal(
            "33333333-cccc-cccc-cccc-333333333333",
            mappings["1003"]
        );
    }
}