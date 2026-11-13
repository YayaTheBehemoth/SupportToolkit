using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.CustomerMapping;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Composition;

/// <summary>
/// Builds the Backup Aggregator workflows for a host. The mode
/// (environment variable, then profile, then the fixture default) is read
/// on every call, so a profile switched while the program runs takes
/// effect on the next review. Tickets always go to the configured
/// Zendesk destination, whatever the review's mode.
/// </summary>
public static class BackupAggregatorComposition
{
    /// <param name="progress">
    /// Where operator-facing progress messages from a production review
    /// go (for example while waiting for in-progress backups). Null
    /// discards them.
    /// </param>
    /// <param name="runMode">
    /// Manual (the default) never waits for in-progress backups;
    /// Automatic waits as the profile says.
    /// </param>
    public static BackupAggregatorWorkflow Workflow(
        SupportToolkitConfigurationResolver configurationResolver,
        Action<string>? progress = null,
        ReviewRunMode runMode = ReviewRunMode.Manual)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        return new BackupAggregatorWorkflow(
            new ModeSelectedSources(
                configurationResolver,
                new FixtureBackupAggregatorSources(),
                new ProductionBackupAggregatorSources(
                    configurationResolver,
                    progress,
                    runMode)),
            new ZendeskBackupTicketSink(
                configurationResolver));
    }

    /// <summary>
    /// The monitored review. Every optional source replaces the
    /// production default (tests, other hosts); fixture mode ignores
    /// them and runs entirely on synthetic data.
    /// </summary>
    public static BackupAggregatorScopedWorkflow ScopedWorkflow(
        SupportToolkitConfigurationResolver configurationResolver,
        IMonitoringIdentitySource? identitySource = null,
        Func<CancellationToken,
            Task<ISmartSubscriptionEvidenceSource>>?
            evidenceSourceFactory = null,
        Func<CancellationToken,
            Task<ScopedInventoryRuntime>>?
            inventoryRuntimeFactory = null,
        ICustomerMappingRepository? mappingRepository = null,
        Func<CancellationToken,
            Task<ICustomerNameSource>>?
            nameSourceFactory = null,
        Action<string>? progress = null,
        ReviewRunMode runMode = ReviewRunMode.Manual)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        return new BackupAggregatorScopedWorkflow(
            new ModeSelectedScopedSources(
                configurationResolver,
                new FixtureScopedReviewSources(),
                new ProductionScopedReviewSources(
                    configurationResolver,
                    identitySource,
                    evidenceSourceFactory,
                    inventoryRuntimeFactory,
                    mappingRepository,
                    nameSourceFactory,
                    progress,
                    runMode)));
    }

    private static Task<SupportToolkitMode> ModeAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken) =>
        resolver.GetModuleModeAsync(
            BackupAggregatorModule.ModuleCommand,
            cancellationToken);

    private sealed class ModeSelectedSources(
        SupportToolkitConfigurationResolver resolver,
        IBackupAggregatorSources fixture,
        IBackupAggregatorSources production)
        : IBackupAggregatorSources
    {
        public Task<SupportToolkitMode> GetModeAsync(
            CancellationToken cancellationToken) =>
            ModeAsync(resolver, cancellationToken);

        public async Task<AggregatedBackupReport> ReviewAllTenantsAsync(
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .ReviewAllTenantsAsync(cancellationToken);

        public async Task<AggregatedBackupReport> ReviewTenantAsync(
            string tenantName,
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .ReviewTenantAsync(tenantName, cancellationToken);

        private async Task<IBackupAggregatorSources> SelectAsync(
            CancellationToken cancellationToken) =>
            await ModeAsync(resolver, cancellationToken) == SupportToolkitMode.Fixture
                ? fixture
                : production;
    }

    private sealed class ModeSelectedScopedSources(
        SupportToolkitConfigurationResolver resolver,
        IScopedReviewSources fixture,
        IScopedReviewSources production)
        : IScopedReviewSources
    {
        public async Task<ScopedReviewAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .GetAvailabilityAsync(cancellationToken);

        public async Task<ScopedReviewInputs> GetInputsAsync(
            DateOnly asOfDate,
            CancellationToken cancellationToken) =>
            await (await SelectAsync(cancellationToken))
                .GetInputsAsync(asOfDate, cancellationToken);

        private async Task<IScopedReviewSources> SelectAsync(
            CancellationToken cancellationToken) =>
            await ModeAsync(resolver, cancellationToken) == SupportToolkitMode.Fixture
                ? fixture
                : production;
    }
}
