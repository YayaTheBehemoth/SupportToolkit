using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Tenants;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Microsoft365;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// The backup inventory read from Acronis: tenants through the tenant
/// resolver, and each tenant's Microsoft 365 and device resources through
/// the inventory providers, normalized into the module's report entries.
/// </summary>
public sealed class AcronisBackupInventorySource : IBackupInventorySource
{
    private readonly AcronisTenantResolver _tenantResolver;
    private readonly TenantInventoryReviewService _tenantReviewer;

    public AcronisBackupInventorySource(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer)
    {
        _tenantResolver = tenantResolver;
        _tenantReviewer =
            new TenantInventoryReviewService(
                microsoft365Inventory,
                deviceInventory,
                normalizer);
    }

    public Task<TenantRecord> ResolveExactAsync(
        string tenantName,
        CancellationToken cancellationToken = default) =>
        _tenantResolver.ResolveExactAsync(tenantName, cancellationToken);

    public Task<IReadOnlyList<TenantRecord>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default) =>
        _tenantResolver.GetAllTenantsAsync(cancellationToken);

    public Task<IReadOnlyList<TenantRecord>> GetCustomerTenantsAsync(
        CancellationToken cancellationToken = default) =>
        _tenantResolver.GetCustomerTenantsAsync(cancellationToken);

    public Task<TenantReviewResult> ReviewTenantAsync(
        TenantRecord tenant,
        CancellationToken cancellationToken = default) =>
        _tenantReviewer.ReviewResolvedTenantAsync(tenant, cancellationToken);
}

/// <summary>
/// Builds a review service that reads Acronis. Takes exactly what the
/// review service used to take, so every caller is unchanged apart from
/// the call.
/// </summary>
public static class AcronisBackupInventory
{
    public static BackupInventoryReviewService CreateReviewService(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer,
        OperationalLogger? logger = null,
        int maxConcurrentTenants = BackupInventoryReviewService.DefaultMaxConcurrentTenants,
        InProgressWaitOptions? inProgressWait = null,
        TimeProvider? timeProvider = null,
        Action<string>? progress = null) =>
        new(
            new AcronisBackupInventorySource(
                tenantResolver,
                microsoft365Inventory,
                deviceInventory,
                normalizer),
            logger,
            maxConcurrentTenants,
            inProgressWait,
            timeProvider,
            progress);
}
