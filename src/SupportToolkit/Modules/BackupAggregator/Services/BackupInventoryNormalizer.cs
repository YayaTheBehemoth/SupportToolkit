using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Combines the independently normalized Microsoft 365 and device inventories
/// into one tenant report.
/// </summary>
public sealed class BackupInventoryNormalizer
{
    private readonly Microsoft365BackupResourceNormalizer
        _microsoft365Normalizer;

    private readonly DeviceBackupResourceNormalizer
        _deviceNormalizer;

    public BackupInventoryNormalizer(
        Microsoft365BackupResourceNormalizer? microsoft365Normalizer = null,
        DeviceBackupResourceNormalizer? deviceNormalizer = null)
    {
        _microsoft365Normalizer =
            microsoft365Normalizer
            ?? new Microsoft365BackupResourceNormalizer();

        _deviceNormalizer =
            deviceNormalizer
            ?? new DeviceBackupResourceNormalizer();
    }

    public TenantBackupReport BuildTenantReport(
        string tenantName,
        IReadOnlyList<AcronisMicrosoft365ResourceDto> microsoft365Resources,
        IReadOnlyList<AcronisDeviceResourceDto> deviceResources)
    {
        var entries =
            _microsoft365Normalizer
                .Normalize(microsoft365Resources)
                .Concat(
                    _deviceNormalizer.Normalize(deviceResources)
                )
                .ToList()
                .AsReadOnly();

        return new TenantBackupReport
        {
            TenantName = tenantName,
            Entries = entries
        };
    }
}
