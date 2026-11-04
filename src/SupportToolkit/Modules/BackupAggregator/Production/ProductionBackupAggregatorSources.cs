using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// Production mode for the all-tenant review: Acronis, read-only, through
/// the active profile's connection. A session is opened per review.
/// </summary>
public sealed class ProductionBackupAggregatorSources : IBackupAggregatorSources
{
    private readonly SupportToolkitConfigurationResolver _configurationResolver;
    private readonly Action<string>? _progress;
    private readonly ReviewRunMode _runMode;

    public ProductionBackupAggregatorSources(
        SupportToolkitConfigurationResolver configurationResolver,
        Action<string>? progress,
        ReviewRunMode runMode)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(nameof(configurationResolver));

        _progress = progress;
        _runMode = runMode;
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Production);

    public async Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken)
    {
        using var session =
            await CreateSessionAsync(
                cancellationToken
            );

        return await session.ReviewService
            .ReviewAllTenantsAsync(
                cancellationToken
            );
    }

    public async Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken)
    {
        using var session =
            await CreateSessionAsync(
                cancellationToken
            );

        return await session.ReviewService
            .ReviewTenantAsync(
                tenantName,
                cancellationToken
            );
    }

    private Task<BackupAggregatorProductionSession>
        CreateSessionAsync(
            CancellationToken cancellationToken) =>
        BackupAggregatorProduction.CreateSessionAsync(
            _configurationResolver,
            _runMode,
            _progress,
            cancellationToken);
}

/// <summary>
/// Production mode for the monitored review: the profile's items and
/// exclusions, Business Central evidence and names, and the Acronis
/// review session. Any source can be replaced (tests, other hosts).
///
/// Order is deliberate: local configuration is read before any sign-in
/// or API request, so a missing setting fails with no external activity.
/// </summary>
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
                    "The monitored review needs a Business Central " +
                    "connection in the active profile.");
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

/// <summary>
/// Tickets go to the configured Zendesk destination.
/// </summary>
public sealed class ZendeskBackupTicketSink : IBackupTicketSink
{
    private readonly SupportToolkitConfigurationResolver _configurationResolver;

    public ZendeskBackupTicketSink(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(nameof(configurationResolver));
    }

    public Task<TicketDestinationPreview> GetDestinationAsync(
        CancellationToken cancellationToken) =>
        BackupAggregatorProduction.GetTicketDestinationAsync(
            _configurationResolver,
            cancellationToken);

    public Task<CreatedTicket> SubmitAsync(
        TicketDraft draft,
        TicketDestinationPreview? confirmedDestination,
        CancellationToken cancellationToken) =>
        BackupAggregatorProduction.SubmitTicketDraftAsync(
            _configurationResolver,
            draft,
            confirmedDestination,
            cancellationToken);
}
