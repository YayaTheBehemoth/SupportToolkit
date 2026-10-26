using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// The operator's own company appears in Acronis as a partner tenant
/// and also as a customer tenant carrying the same name. It has no
/// Business Central customer, so the customer tenant must not be
/// treated as a customer to reconcile.
///
/// Detection is structural, not configured: a customer-kind tenant
/// whose name equals a partner-kind tenant's name (trimmed, ignoring
/// case). It applies only when exactly one customer tenant matches;
/// otherwise nothing is excluded and the ambiguity is reported.
/// </summary>
public static class OperatorTenantDetector
{
    public sealed record Detection(
        string? TenantId,
        string? Notice);

    public static Detection Detect(
        IEnumerable<TenantDto> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        var all =
            tenants
                .Where(tenant => tenant is not null)
                .ToArray();

        var partnerNames =
            all
                .Where(tenant => IsKind(tenant, "partner"))
                .Select(tenant => Normalize(tenant.Name))
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (partnerNames.Count == 0)
        {
            return new Detection(null, null);
        }

        var matches =
            all
                .Where(tenant => IsKind(tenant, "customer"))
                .Where(
                    tenant => partnerNames.Contains(
                        Normalize(tenant.Name)))
                .ToArray();

        return matches.Length switch
        {
            0 => new Detection(null, null),
            1 => new Detection(matches[0].Id.Trim(), null),
            _ => new Detection(
                null,
                $"{matches.Length} customer tenants share a partner " +
                "tenant's name; none was excluded as the operator's own.")
        };
    }

    private static bool IsKind(
        TenantDto tenant,
        string kind)
    {
        return string.Equals(
            tenant.Kind,
            kind,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(
        string? name)
    {
        return (name ?? string.Empty)
            .Trim()
            .Normalize(System.Text.NormalizationForm.FormC);
    }
}
