using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.Tenants;

namespace SupportToolkit.Core.CustomerMapping;

/// <summary>
/// Customer parties for a resolution run, with what was left out.
/// PartnerTenants and DeletedCustomerTenants explain the exclusions
/// without printing any value.
/// </summary>
public sealed record TenantPartyBuild(
    IReadOnlyList<CustomerParty> Parties,
    bool OperatorTenantExcluded,
    string? Notice,
    int PartnerTenants = 0,
    int DeletedCustomerTenants = 0);

/// <summary>
/// Converts the backup system's customer tenants into matching evidence:
/// id and name only (the provider mapping explains why nothing else is a
/// usable match key).
///
/// Deleted and non-customer tenants are excluded.
/// </summary>
public static class TenantCustomerParties
{
    /// <summary>
    /// Parties for resolution runs: the operator's own customer tenant
    /// is left out, as detected from the tenant list itself.
    /// </summary>
    public static TenantPartyBuild ForResolution(
        IReadOnlyList<TenantRecord> allTenants)
    {
        ArgumentNullException.ThrowIfNull(allTenants);

        var detection =
            OperatorTenantDetector.Detect(allTenants);

        var customers =
            allTenants
                .Where(
                    tenant =>
                        tenant.Role == TenantRole.Customer
                        && !tenant.DeletedAt.HasValue
                        && !string.Equals(
                            tenant.Id?.Trim(),
                            detection.TenantId,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return new TenantPartyBuild(
            customers
                .Select(
                    tenant => new CustomerParty(
                        tenant.Id,
                        tenant.Name))
                .ToArray(),
            detection.TenantId is not null,
            detection.Notice,
            allTenants.Count(
                tenant => tenant.Role == TenantRole.Partner),
            allTenants.Count(
                tenant => tenant.Role == TenantRole.Customer
                    && tenant.DeletedAt.HasValue));
    }

    public static IReadOnlyList<CustomerParty> FromCustomerTenants(
        IEnumerable<TenantRecord> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        return tenants
            .Where(
                tenant =>
                    tenant.Role == TenantRole.Customer
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
