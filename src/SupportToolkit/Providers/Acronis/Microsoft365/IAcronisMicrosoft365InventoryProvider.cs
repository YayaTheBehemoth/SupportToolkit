using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Providers.Acronis.Microsoft365;

/// <summary>
/// Returns one unique inventory row per resolvable Microsoft 365 resource.
/// Resources without a usable identity are preserved rather than discarded.
/// </summary>
public interface IAcronisMicrosoft365InventoryProvider
{
    Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>> GetResourcesForTenantAsync(
        string tenantId,
        CancellationToken cancellationToken = default
    );
}
