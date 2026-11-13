using SupportToolkit.Domain.Reconciliation;
using SupportToolkit.Domain.Tenants;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Converts Acronis customer tenants into provider-neutral
/// reconciliation evidence. Non-customer tenants are excluded, as in
/// tenant selection.
/// </summary>
public static class TenantObservations
{
    public static IReadOnlyList<TenantObservation> FromCustomerTenants(
        IEnumerable<TenantRecord> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        return tenants
            .Where(
                tenant =>
                    tenant.Role == TenantRole.Customer
            )
            .Select(
                tenant =>
                    new TenantObservation(
                        tenant.Id,
                        tenant.Enabled,
                        tenant.DeletedAt.HasValue
                    )
            )
            .ToArray();
    }
}
