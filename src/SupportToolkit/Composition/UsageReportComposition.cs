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
                new ProductionUsageReportSetup(resolver, productionSources, Systems(resolver))),
            progress,
            timeProvider);
    }

    /// <summary>
    /// The profile's backup and commercial systems, selected as the Backup
    /// Aggregator selects them, so both modules read the same systems.
    /// </summary>
    public static UsageReportSystems Systems(SupportToolkitConfigurationResolver resolver)
    {
        var backup = BackupSystemsComposition.ForUsageReport(resolver);
        var commercial = CommercialSystemsComposition.ForUsageReport(resolver);

        return new UsageReportSystems(
            backup.Check,
            commercial.Check,
            commercial.Open,
            backup.Open)
        {
            Names = async token =>
            {
                var (backupName, gbNote, showsTerabytes) = await backup.Name(token);
                var (billingName, quantityName, signsIn) = await commercial.Name(token);

                return new UsageSystemNames(backupName, billingName, quantityName, gbNote, signsIn, showsTerabytes);
            }
        };
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
