using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Composition;

/// <summary>
/// The Backup Aggregator's mode is chosen per call from the configuration
/// as it is then. Fixture mode never touches the production sources, and
/// production fails locally, before any request, when it is not set up.
/// </summary>
public class BackupAggregatorCompositionTests
{
    private static SupportToolkitConfiguration Configuration(
        SupportToolkitMode mode) =>
        new()
        {
            ActiveProfile = "work",
            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        Modules = { ["backup-aggregator"] = mode }
                    }
            }
        };

    [Fact]
    public async Task AllTenantReview_FollowsTheModeAtTheTimeOfEachCall()
    {
        var (_, store, resolver) = Create(Configuration(SupportToolkitMode.Fixture));

        var workflow = BackupAggregatorComposition.Workflow(resolver);

        Assert.Equal(SupportToolkitMode.Fixture, await workflow.GetModeAsync());
        Assert.NotEmpty((await workflow.ReviewAllTenantsAsync()).Tenants);

        store.Configuration.Profiles["work"].Modules["backup-aggregator"] =
            SupportToolkitMode.Production;

        Assert.Equal(SupportToolkitMode.Production, await workflow.GetModeAsync());

        // No Acronis connection: a local configuration error, no request.
        var failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.ReviewAllTenantsAsync());

        Assert.Contains("Acronis", failure.Message);
    }

    [Fact]
    public async Task ScopedReview_InFixtureMode_NeverUsesTheProductionSources()
    {
        var (_, _, resolver) = Create(Configuration(SupportToolkitMode.Fixture));

        var used = 0;

        var workflow =
            BackupAggregatorComposition.ScopedWorkflow(
                resolver,
                evidenceSourceFactory: _ =>
                {
                    used++;
                    throw new InvalidOperationException("production evidence");
                },
                inventoryRuntimeFactory: _ =>
                {
                    used++;
                    throw new InvalidOperationException("production inventory");
                },
                nameSourceFactory: _ =>
                {
                    used++;
                    return Task.FromException<ICustomerNameSource>(
                        new InvalidOperationException("production names"));
                });

        var availability = await workflow.GetAvailabilityAsync();
        var review = await workflow.ReviewAsync(new DateOnly(2026, 10, 15));

        Assert.True(availability.IsAvailable);
        Assert.Equal(SupportToolkitMode.Fixture, availability.Mode);
        Assert.True(review.IsInventoryComplete);
        Assert.Equal(0, used);
    }

    [Fact]
    public async Task ScopedReview_InProduction_ReadsConfigurationBeforeAnySource()
    {
        var (_, _, resolver) = Create(Configuration(SupportToolkitMode.Production));

        var used = 0;

        var workflow =
            BackupAggregatorComposition.ScopedWorkflow(
                resolver,
                evidenceSourceFactory: _ =>
                {
                    used++;
                    throw new InvalidOperationException("production evidence");
                });

        // No monitoring item number configured: fails locally first.
        var failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.ReviewAsync(new DateOnly(2026, 10, 15)));

        Assert.Contains("Monitoring item number", failure.Message);
        Assert.Equal(0, used);
    }
}
