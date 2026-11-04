using SupportToolkit.Domain.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Providers.Acronis.Tenants;

/// <summary>
/// Converts Acronis tenants to the toolkit's own tenant type, at the edge,
/// so module rules never see the Acronis DTO. Acronis's own conventions
/// are applied here and nowhere else: the kinds "customer" and "partner"
/// (any case) become roles, and a tenant ID is canonical only when it is a
/// UUID.
///
/// The contact fields were profiled against production and hold no usable
/// match key (no CVR, no per-customer e-mail, and the account number is
/// Acronis's own numbering), so a tenant carries id and name only.
/// </summary>
public static class TenantRecordMapping
{
    public static TenantRecord ToTenantRecord(this TenantDto tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantRecord
        {
            Id = tenant.Id,
            Name = tenant.Name,
            Kind = tenant.Kind,
            Role = RoleOf(tenant.Kind),
            CanonicalId = CanonicalIdOf(tenant.Id),
            DeletedAt = tenant.DeletedAt,
            Enabled = tenant.Enabled
        };
    }

    public static IReadOnlyList<TenantRecord> ToTenantRecords(
        this IEnumerable<TenantDto> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        return tenants.Select(ToTenantRecord).ToArray();
    }

    private static TenantRole RoleOf(
        string? kind) =>
        string.Equals(kind, "customer", StringComparison.OrdinalIgnoreCase)
            ? TenantRole.Customer
            : string.Equals(kind, "partner", StringComparison.OrdinalIgnoreCase)
                ? TenantRole.Partner
                : TenantRole.Other;

    private static string? CanonicalIdOf(
        string? id) =>
        Guid.TryParse(id, out var parsed)
            ? parsed.ToString("D")
            : null;
}
