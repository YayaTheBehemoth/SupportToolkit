using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class AcronisTenantResolver
{
    private readonly IAcronisTenantProvider _tenantProvider;

    public AcronisTenantResolver(
        IAcronisTenantProvider tenantProvider)
    {
        _tenantProvider = tenantProvider;
    }

    public async Task<TenantDto> ResolveExactAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var tenants =
            await _tenantProvider.GetTenantsAsync(
                cancellationToken
            );

        var matches =
            tenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.Name,
                            tenantName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"No tenant named '{tenantName}' was found."
            );
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"More than one tenant named '{tenantName}' was found. " +
                "SupportToolkit will not guess which tenant to use."
            );
        }

        var tenant =
            matches[0];

        if (!Guid.TryParse(tenant.Id, out _))
        {
            throw new InvalidOperationException(
                "The matched tenant does not contain the UUID required " +
                "for customer-scoped authentication."
            );
        }

        return tenant;
    }
}
