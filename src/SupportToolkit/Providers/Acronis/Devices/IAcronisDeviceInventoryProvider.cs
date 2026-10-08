using SupportToolkit.Providers.Acronis.Devices.Dtos;

namespace SupportToolkit.Providers.Acronis.Devices;

public interface IAcronisDeviceInventoryProvider
{
    Task<IReadOnlyList<AcronisDeviceResourceDto>> GetResourcesForTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    );
}
