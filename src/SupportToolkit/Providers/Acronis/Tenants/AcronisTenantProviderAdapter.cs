using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Providers.Acronis.Tenants;

/// <summary>
/// Narrow tenant-discovery adapter over the broader legacy Acronis provider.
/// </summary>
public sealed class AcronisTenantProviderAdapter
    : IAcronisTenantProvider
{
    private readonly HttpAcronisProvider _provider;

    public AcronisTenantProviderAdapter(
        HttpAcronisProvider provider)
    {
        _provider = provider;
    }

    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return _provider.GetTenantsAsync(
            cancellationToken
        );
    }
}
