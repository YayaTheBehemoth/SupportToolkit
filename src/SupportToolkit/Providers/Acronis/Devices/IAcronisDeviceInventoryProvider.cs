using SupportToolkit.Providers.Acronis.Devices.Dtos;

namespace SupportToolkit.Providers.Acronis.Devices;

/// <summary>
/// Returns one unique inventory row per resolvable Acronis device resource.
/// Resources without a usable identity are preserved rather than discarded.
/// </summary>
public interface IAcronisDeviceInventoryProvider
{
    Task<IReadOnlyList<AcronisDeviceResourceDto>> GetResourcesForTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    );
}
