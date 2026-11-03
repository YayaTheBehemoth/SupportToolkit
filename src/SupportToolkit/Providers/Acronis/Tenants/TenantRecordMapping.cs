using SupportToolkit.Core.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Providers.Acronis.Tenants;

/// <summary>
/// Converts Acronis tenants to the toolkit's own tenant type, at the edge,
/// so module rules never see the Acronis DTO. Only the fields the rules use
/// are carried; nothing is interpreted.
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
}
