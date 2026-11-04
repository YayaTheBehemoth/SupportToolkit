using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Services;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Composition;

/// <summary>
/// The composition root chooses fixture or production per run, from the
/// configuration as it is at that moment, and creating production sources
/// is deferred until a run actually starts.
/// </summary>
public class UsageReportCompositionTests
{
    [Fact]
    public async Task ModeChangedWhileRunning_TakesEffectOnTheNextCall()
    {
        var configuration = FullConfiguration(SupportToolkitMode.Fixture);
        configuration.Profiles["work"].Modules["usage-report"] = SupportToolkitMode.Fixture;

        var (_, store, resolver) = Create(configuration, FullSecrets());

        var opened = 0;

        var workflow =
            UsageReportComposition.Workflow(
                resolver,
                productionSources: _ =>
                {
                    opened++;
                    throw new InvalidOperationException("production sources");
                });

        Assert.Equal(SupportToolkitMode.Fixture, await workflow.GetModeAsync());

        var fixtureReport = await workflow.GenerateAsync();

        Assert.Equal(SupportToolkitMode.Fixture, fixtureReport.Mode);
        Assert.Equal(0, opened);

        // Switch the profile to production while the workflow lives on.
        store.Configuration.Profiles["work"].Modules["usage-report"] =
            SupportToolkitMode.Production;
        store.Configuration.Profiles["work"].MonitoringScope["usage-report"] =
            new ModuleMonitoringScopeConfiguration
            {
                ServiceItemNumbers = ["EXAMPLE-BACKUP"]
            };

        Assert.Equal(SupportToolkitMode.Production, await workflow.GetModeAsync());

        var failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.GenerateAsync());

        Assert.Equal("production sources", failure.Message);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task Availability_InProduction_ChecksLocalConfigurationOnly()
    {
        var configuration = FullConfiguration(SupportToolkitMode.Production);
        configuration.Profiles["work"].Modules["usage-report"] = SupportToolkitMode.Production;

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var opened = 0;

        var availability =
            await UsageReportComposition.Workflow(
                    resolver,
                    productionSources: _ =>
                    {
                        opened++;
                        throw new InvalidOperationException("never");
                    })
                .GetAvailabilityAsync();

        // No backup items configured: unavailable, with the reason, and
        // nothing was opened.
        Assert.Equal(SupportToolkitMode.Production, availability.Mode);
        Assert.False(availability.IsAvailable);
        Assert.Contains("No backup items", availability.Reason);
        Assert.Equal(0, opened);
    }
}
