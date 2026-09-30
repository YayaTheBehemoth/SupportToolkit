using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

public interface IAcronisProvider
{
    Task<IReadOnlyList<ResourceStatusDto>> GetResourceStatusesAsync(
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default
    );
}