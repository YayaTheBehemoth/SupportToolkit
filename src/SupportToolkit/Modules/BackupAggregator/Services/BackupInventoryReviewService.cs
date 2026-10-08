using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Microsoft365;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupInventoryReviewService
{
    private readonly AcronisTenantResolver _tenantResolver;
    private readonly IAcronisMicrosoft365InventoryProvider
        _microsoft365Inventory;
    private readonly IAcronisDeviceInventoryProvider
        _deviceInventory;
    private readonly BackupInventoryNormalizer _normalizer;
    private readonly OperationalLogger? _logger;

    public BackupInventoryReviewService(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer,
        OperationalLogger? logger = null)
    {
        _tenantResolver = tenantResolver;
        _microsoft365Inventory = microsoft365Inventory;
        _deviceInventory = deviceInventory;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Starting backup inventory review."
        );

        var tenant =
            await _tenantResolver.ResolveExactAsync(
                tenantName,
                cancellationToken
            );

        _logger?.Info(
            "Tenant resolved."
        );

        var microsoft365Resources =
            await _microsoft365Inventory.GetResourcesForTenantAsync(
                tenant.Id,
                cancellationToken
            );

        _logger?.Info(
            $"Microsoft 365 resources: {microsoft365Resources.Count}."
        );

        var deviceResources =
            await _deviceInventory.GetResourcesForTenantAsync(
                tenant.Id,
                cancellationToken
            );

        _logger?.Info(
            $"Device resources: {deviceResources.Count}."
        );

        var tenantReport =
            _normalizer.BuildTenantReport(
                tenant.Name,
                microsoft365Resources,
                deviceResources
            );

        var report =
            new AggregatedBackupReport
            {
                Tenants = [tenantReport]
            };

        _logger?.Info(
            $"Backup inventory review complete: {report.RowsChecked} checked, " +
            $"{report.FindingsCount} require attention, " +
            $"{report.UnknownRowsCount} unclassified."
        );

        return report;
    }
}
