using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Loads and normalizes one tenant's inventories sequentially.
/// Estate-wide scheduling and failure reporting belong to BackupInventoryReviewService.
/// </summary>
internal sealed class TenantInventoryReviewService
{
    private readonly IAcronisMicrosoft365InventoryProvider _microsoft365Inventory;
    private readonly IAcronisDeviceInventoryProvider _deviceInventory;
    private readonly BackupInventoryNormalizer _normalizer;

    public TenantInventoryReviewService(
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer)
    {
        _microsoft365Inventory = microsoft365Inventory;
        _deviceInventory = deviceInventory;
        _normalizer = normalizer;
    }

    public async Task<TenantReviewResult>
        ReviewResolvedTenantAsync(
            TenantDto tenant,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<AcronisMicrosoft365ResourceDto>
            microsoft365Resources;

        try
        {
            microsoft365Resources =
                await _microsoft365Inventory
                    .GetResourcesForTenantAsync(
                        tenant.Id,
                        cancellationToken
                    );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TenantReviewStageException(
                "Microsoft 365 inventory",
                exception
            );
        }

        IReadOnlyList<AcronisDeviceResourceDto>
            deviceResources;

        try
        {
            deviceResources =
                await _deviceInventory
                    .GetResourcesForTenantAsync(
                        tenant.Id,
                        cancellationToken
                    );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TenantReviewStageException(
                "Device inventory",
                exception
            );
        }

        var tenantReport =
            _normalizer.BuildTenantReport(
                tenant.Name,
                microsoft365Resources,
                deviceResources
            );

        return new TenantReviewResult(
            tenantReport,
            microsoft365Resources.Count,
            deviceResources.Count
        );
    }

}

internal sealed record TenantReviewResult(
    TenantBackupReport Report,
    int Microsoft365ResourceCount,
    int DeviceResourceCount
);

internal sealed class TenantReviewStageException : Exception
{
    public string Stage { get; }

    public TenantReviewStageException(
        string stage,
        Exception innerException)
        : base($"{stage} failed.", innerException)
    {
        Stage = stage;
    }
}
