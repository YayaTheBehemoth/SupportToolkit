using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Core.Tenants;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Converts Acronis customer tenants into provider-neutral
/// reconciliation evidence. Non-customer tenants are excluded, as in
/// tenant selection.
/// </summary>
public static class AcronisTenantObservations
{
    public static IReadOnlyList<AcronisTenantObservation> FromCustomerTenants(
        IEnumerable<TenantRecord> tenants)
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
            )
            .Select(
                tenant =>
                    new AcronisTenantObservation(
                        tenant.Id,
                        tenant.Enabled,
                        tenant.DeletedAt.HasValue
                    )
            )
            .ToArray();
    }
}
