using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Providers.Acronis.Microsoft365;

public interface IAcronisMicrosoft365InventoryProvider
{
    Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>> GetResourcesForTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    );
}
