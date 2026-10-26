using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// An opt-in review limited to tenants the shared monitoring scope
/// selects.
///
/// Separate from BackupAggregatorWorkflow on purpose: the existing
/// all-tenant review and the ticket workflow do not depend on, call
/// or change with this class. A ticket is never built from a scoped
/// result.
///
/// The feature is available only when the active profile selects a
/// Business Central connection. Fixture mode runs entirely on synthetic data.
/// </summary>
public sealed class BackupAggregatorScopedWorkflow
{
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    private readonly IMonitoringIdentitySource _identitySource;

    private readonly Func<CancellationToken,
        Task<ISmartSubscriptionEvidenceSource>> _evidenceSourceFactory;

    private readonly Func<CancellationToken,
        Task<ScopedInventoryRuntime>> _inventoryRuntimeFactory;

    public BackupAggregatorScopedWorkflow(
        SupportToolkitConfigurationResolver configurationResolver,
        IMonitoringIdentitySource? identitySource = null,
        Func<CancellationToken,
            Task<ISmartSubscriptionEvidenceSource>>?
            evidenceSourceFactory = null,
        Func<CancellationToken,
            Task<ScopedInventoryRuntime>>?
            inventoryRuntimeFactory = null)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));

        _identitySource =
            identitySource
            ?? new UnavailableMonitoringIdentitySource();

        _evidenceSourceFactory =
            evidenceSourceFactory
            ?? CreateBusinessCentralEvidenceSourceAsync;

        _inventoryRuntimeFactory =
            inventoryRuntimeFactory
            ?? CreateAcronisRuntimeAsync;
    }

    /// <summary>
    /// Local check only: whether the active profile selects a
    /// Business Central connection.
    /// </summary>
    public Task<bool> IsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        return _configurationResolver
            .IsMonitoringScopeEnabledAsync(cancellationToken);
    }

    /// <summary>
    /// Scoped review according to the BackupAggregator runtime mode.
    ///
    /// Fixture mode uses synthetic subscriptions, tenants and
    /// inventory and makes no external request.
    ///
    /// Production order is deliberate: local configuration and the
    /// identity source are checked before any sign-in or API request,
    /// so an unavailable identity source fails with no external
    /// activity.
    /// </summary>
    public async Task<ScopedBackupInventoryReviewResult> ReviewAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var mode =
            await _configurationResolver.GetModuleModeAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        if (mode == SupportToolkitMode.Fixture)
        {
            var inventory =
                new HealthyFixtureInventoryProvider(asOfDate);

            return await ExecuteAsync(
                [SmartSubscriptionFixtureFactory
                    .FixtureMonitoringItemNumber],
                new FixtureMonitoringIdentitySource(),
                _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                    new FixtureSmartSubscriptionEvidenceSource()),
                _ => Task.FromResult(
                    new ScopedInventoryRuntime(
                        new BackupInventoryReviewService(
                            new AcronisTenantResolver(
                                new FixtureCustomerTenantProvider()),
                            inventory,
                            inventory,
                            new BackupInventoryNormalizer()),
                        null)),
                asOfDate,
                cancellationToken);
        }

        var itemNumbers =
            await _configurationResolver
                .GetMonitoringItemNumbersAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        var excludedTenantIds =
            await _configurationResolver
                .GetExcludedTenantIdsAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        return await ExecuteAsync(
            itemNumbers,
            _identitySource,
            _evidenceSourceFactory,
            _inventoryRuntimeFactory,
            asOfDate,
            cancellationToken,
            excludedTenantIds);
    }

    private static async Task<ScopedBackupInventoryReviewResult>
        ExecuteAsync(
            IReadOnlyCollection<string> itemNumbers,
            IMonitoringIdentitySource identitySource,
            Func<CancellationToken,
                Task<ISmartSubscriptionEvidenceSource>>
                evidenceSourceFactory,
            Func<CancellationToken, Task<ScopedInventoryRuntime>>
                inventoryRuntimeFactory,
            DateOnly asOfDate,
            CancellationToken cancellationToken,
            IReadOnlySet<string>? excludedTenantIds = null)
    {
        // Fails closed here when no production source exists.
        var identities =
            await identitySource.GetIdentitiesAsync(
                cancellationToken);

        var evidenceSource =
            await evidenceSourceFactory(cancellationToken);

        var evidence =
            await evidenceSource.GetEvidenceAsync(
                cancellationToken);

        var observations =
            new SmartSubscriptionEntitlementEvaluator(itemNumbers)
                .Evaluate(
                    evidence.Headers,
                    evidence.Lines,
                    asOfDate);

        var runtime =
            await inventoryRuntimeFactory(cancellationToken);

        using (runtime.Lifetime)
        {
            var tenants =
                await runtime.ReviewService
                    .GetCustomerTenantsAsync(cancellationToken);

            var selection =
                new MonitoringTenantSelector()
                    .Select(
                        tenants,
                        observations,
                        identities,
                        excludedTenantIds);

            return await runtime.ReviewService
                .ReviewSelectedTenantsAsync(
                    selection,
                    cancellationToken);
        }
    }

    private async Task<ISmartSubscriptionEvidenceSource>
        CreateBusinessCentralEvidenceSourceAsync(
            CancellationToken cancellationToken)
    {
        var bundle =
            await BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);

        return new BusinessCentralSmartSubscriptionEvidenceSource(
            bundle.Provider,
            BusinessCentralSourceContext.From(bundle.Options));
    }

    private async Task<ScopedInventoryRuntime>
        CreateAcronisRuntimeAsync(
            CancellationToken cancellationToken)
    {
        var connection =
            await _configurationResolver
                .GetAcronisConnectionAsync(cancellationToken);

        var session =
            BackupAggregatorProductionSession.Create(
                AcronisOptions.Create(
                    connection.DatacenterUrl,
                    connection.ClientId,
                    connection.ClientSecret),
                OperationalLogger.FromEnvironment());

        return new ScopedInventoryRuntime(
            session.ReviewService,
            session);
    }
}

/// <summary>
/// The inventory review service and the lifetime that owns its
/// transport.
/// </summary>
public sealed record ScopedInventoryRuntime(
    BackupInventoryReviewService ReviewService,
    IDisposable? Lifetime);
