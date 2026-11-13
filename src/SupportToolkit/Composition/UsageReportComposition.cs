using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport;
using SupportToolkit.Modules.UsageReport.Fixtures;
using SupportToolkit.Modules.UsageReport.Production;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Composition;

/// <summary>
/// Builds the usage report for a host. The mode (environment variable,
/// then profile, then the fixture default) is read on every call, so a
/// profile switched while the program runs takes effect on the next run.
/// </summary>
public static class UsageReportComposition
{
    public static UsageReportWorkflow Workflow(
        SupportToolkitConfigurationResolver resolver,
        Action<string>? progress = null,
        Func<CancellationToken, Task<IUsageReportSources>>? productionSources = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new UsageReportWorkflow(
            new ModeSelectedUsageReportSetup(
                resolver,
                new FixtureUsageReportSetup(),
                new ProductionUsageReportSetup(resolver, productionSources)),
            progress,
            timeProvider);
    }

    private sealed class ModeSelectedUsageReportSetup(
        SupportToolkitConfigurationResolver resolver,
        IUsageReportSetup fixture,
        IUsageReportSetup production)
        : IUsageReportSetup
    {
        public Task<SupportToolkitMode> GetModeAsync(
            CancellationToken cancellationToken) =>
            resolver.GetModuleModeAsync(
                UsageReportModule.ModuleCommand,
                cancellationToken);

        public async Task<UsageReportAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .GetAvailabilityAsync(cancellationToken);

        public async Task<UsageReportRun> OpenAsync(
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .OpenAsync(cancellationToken);

        private async Task<IUsageReportSetup> SelectAsync(
            CancellationToken cancellationToken) =>
            await GetModeAsync(cancellationToken) == SupportToolkitMode.Fixture
                ? fixture
                : production;
    }
}
