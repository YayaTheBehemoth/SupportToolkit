using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Providers.Acronis.Tenants;

public interface IAcronisTenantProvider
{
    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default
    );
}
