using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.CustomerMapping;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// The all-tenant review against the backup system the profile selects.
/// Which system that is, is decided by the runtime factory the host
/// supplies; this class only runs the review and releases the runtime.
/// </summary>
public sealed class ProductionBackupAggregatorSources : IBackupAggregatorSources
{
    private readonly Func<CancellationToken, Task<ScopedInventoryRuntime>> _runtimeFactory;

    public ProductionBackupAggregatorSources(
        Func<CancellationToken, Task<ScopedInventoryRuntime>> runtimeFactory)
    {
        _runtimeFactory =
            runtimeFactory
            ?? throw new ArgumentNullException(nameof(runtimeFactory));
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Production);

    public Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken) =>
        WithRuntimeAsync(
            (service, token) => service.ReviewAllTenantsAsync(token),
            cancellationToken);

    public Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken) =>
        WithRuntimeAsync(
            (service, token) => service.ReviewTenantAsync(tenantName, token),
            cancellationToken);

    private async Task<AggregatedBackupReport> WithRuntimeAsync(
        Func<BackupInventoryReviewService, CancellationToken, Task<AggregatedBackupReport>> review,
        CancellationToken cancellationToken)
    {
        var runtime = await _runtimeFactory(cancellationToken);

        try
        {
            return await review(runtime.ReviewService, cancellationToken);
        }
        finally
        {
            runtime.Lifetime?.Dispose();
        }
    }
}

public sealed class ProductionScopedReviewSources : IScopedReviewSources
{
    private readonly SupportToolkitConfigurationResolver _configurationResolver;
    private readonly IMonitoringIdentitySource? _identitySource;
    private readonly ICustomerMappingRepository? _mappingRepository;
    private readonly Func<CancellationToken, Task<ISmartSubscriptionEvidenceSource>> _evidenceSourceFactory;
    private readonly Func<CancellationToken, Task<ICustomerNameSource>> _nameSourceFactory;
    private readonly Func<CancellationToken, Task<ScopedInventoryRuntime>> _inventoryRuntimeFactory;

    public ProductionScopedReviewSources(
        SupportToolkitConfigurationResolver configurationResolver,
        IMonitoringIdentitySource? identitySource = null,
        Func<CancellationToken, Task<ISmartSubscriptionEvidenceSource>>? evidenceSourceFactory = null,
        Func<CancellationToken, Task<ScopedInventoryRuntime>>? inventoryRuntimeFactory = null,
        ICustomerMappingRepository? mappingRepository = null,
        Func<CancellationToken, Task<ICustomerNameSource>>? nameSourceFactory = null,
        Action<string>? progress = null,
        ReviewRunMode runMode = ReviewRunMode.Manual)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(nameof(configurationResolver));

        /*
         * An explicitly supplied identity source decides identities
         * as before (fixtures, tests). Otherwise they are resolved
         * automatically each run from Business Central customers and
         * Acronis tenants, with any stored mappings taking precedence.
         */
        _identitySource = identitySource;
        _mappingRepository = mappingRepository;

        var production =
            new ScopedReviewProductionSources(
                configurationResolver,
                runMode,
                progress);

        _evidenceSourceFactory =
            evidenceSourceFactory
            ?? production.CreateEvidenceSourceAsync;

        _nameSourceFactory =
            nameSourceFactory
            ?? production.CreateNameSourceAsync;

        _inventoryRuntimeFactory =
            inventoryRuntimeFactory
            ?? production.CreateInventoryRuntimeAsync;
    }

    public async Task<ScopedReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var mode = SupportToolkitMode.Production;

        try
        {
            return await _configurationResolver
                    .IsMonitoringScopeEnabledAsync(cancellationToken)
                ? new ScopedReviewAvailability(mode, true, null)
                : new ScopedReviewAvailability(
                    mode,
                    false,
                    "The monitored review needs a commercial system in " +
                    "the active profile: a Business Central connection, " +
                    "or the profile's commercial setting.");
        }
        catch (InvalidOperationException exception)
        {
            return new ScopedReviewAvailability(
                mode,
                false,
                exception.Message);
        }
    }

    public async Task<ScopedReviewInputs> GetInputsAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
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

        var serviceItemNumbers =
            await _configurationResolver
                .GetServiceItemNumbersAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        return new ScopedReviewInputs(
            itemNumbers,
            _identitySource,
            _evidenceSourceFactory,
            _nameSourceFactory,
            _inventoryRuntimeFactory,
            excludedTenantIds,
            _mappingRepository,
            serviceItemNumbers);
    }
}
