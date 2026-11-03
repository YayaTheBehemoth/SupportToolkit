using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Customer parties for a resolution run, with what was left out.
/// PartnerTenants and DeletedCustomerTenants explain the exclusions
/// without printing any value.
/// </summary>
public sealed record AcronisPartyBuild(
    IReadOnlyList<CustomerParty> Parties,
    bool OperatorTenantExcluded,
    string? Notice,
    int PartnerTenants = 0,
    int DeletedCustomerTenants = 0);

/// <summary>
/// Converts Acronis customer tenants into matching evidence.
///
/// Only fields of the production-verified TenantDto contract are used.
/// The contact fields were profiled against production and hold no
/// usable match key (no CVR, no per-customer e-mail, and the account
/// number is Acronis's own numbering), and customer_id is absent.
/// Acronis parties therefore carry id and name only.
///
/// Deleted and non-customer tenants are excluded.
/// </summary>
public static class AcronisCustomerParties
{
    /// <summary>
    /// Parties for resolution runs: the operator's own customer tenant
    /// is left out, as detected from the tenant list itself.
    /// </summary>
    public static AcronisPartyBuild ForResolution(
        IReadOnlyList<TenantDto> allTenants)
    {
        ArgumentNullException.ThrowIfNull(allTenants);

        var detection =
            OperatorTenantDetector.Detect(allTenants);

        var customers =
            allTenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.Kind,
                            "customer",
                            StringComparison.OrdinalIgnoreCase)
                        && !tenant.DeletedAt.HasValue
                        && !string.Equals(
                            tenant.Id?.Trim(),
                            detection.TenantId,
                            StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return new AcronisPartyBuild(
            customers
                .Select(
                    tenant => new CustomerParty(
                        tenant.Id,
                        tenant.Name))
                .ToArray(),
            detection.TenantId is not null,
            detection.Notice,
            allTenants.Count(
                tenant => string.Equals(
                    tenant.Kind,
                    "partner",
                    StringComparison.OrdinalIgnoreCase)),
            allTenants.Count(
                tenant => string.Equals(
                        tenant.Kind,
                        "customer",
                        StringComparison.OrdinalIgnoreCase)
                    && tenant.DeletedAt.HasValue));
    }

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
