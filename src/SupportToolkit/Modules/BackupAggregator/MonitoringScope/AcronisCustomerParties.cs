using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Converts Acronis customer tenants into matching evidence.
///
/// Only fields of the production-verified TenantDto contract are used.
/// That contract has no CVR or e-mail field: contacts are untyped and
/// their shape is unverified, and customer_id is absent in production.
/// Acronis parties therefore carry id and name only, and only the Name
/// key can produce matches until another verified field exists.
///
/// Deleted and non-customer tenants are excluded.
/// </summary>
public static class AcronisCustomerParties
{
    public static IReadOnlyList<CustomerParty> FromCustomerTenants(
        IEnumerable<TenantDto> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        return tenants
            .Where(
                tenant =>
                    string.Equals(
                        tenant.Kind,
                        "customer",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && !tenant.DeletedAt.HasValue
            )
            .Select(
                tenant =>
                    new CustomerParty(
                        tenant.Id,
                        tenant.Name
                    )
            )
            .ToArray();
    }
}
