using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// The operator's own company appears in Acronis as a partner tenant
/// and also as a customer tenant carrying a near-identical name. The
/// customer-kind tenant is the one that is reviewed, always, because
/// it is billed to no one and so has no Business Central customer.
/// The partner tenant cannot be reviewed (Acronis refuses it) and
/// serves only as the anchor that identifies the customer tenant.
///
/// Detection is structural, not configured: a customer-kind tenant
/// whose name matches a partner-kind tenant's name once case,
/// punctuation, legal forms and generic words are ignored. An
/// identical-after-normalization match is preferred; a name that
/// merely contains or is contained in the partner's name is accepted
/// only when no identical match exists.
///
/// It applies only when exactly one customer tenant matches at the
/// best tier; otherwise nothing is excluded and the ambiguity is
/// reported.
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

        var partners =
            all
                .Where(tenant => IsKind(tenant, "partner"))
                .ToArray();

        if (partners.Length == 0)
        {
            return new Detection(null, null);
        }

        var candidates =
            all
                .Where(tenant => IsKind(tenant, "customer"))
                .Select(
                    tenant => (
                        Tenant: tenant,
                        Tier: partners
                            .Select(
                                partner => LooseNameMatcher.Compare(
                                    tenant.Name,
                                    partner.Name))
                            .DefaultIfEmpty(LooseNameTier.None)
                            .Max()))
                .Where(
                    item => item.Tier
                        is LooseNameTier.Exact
                        or LooseNameTier.NearExact
                        or LooseNameTier.Containment)
                .ToArray();

        if (candidates.Length == 0)
        {
            return new Detection(null, null);
        }

        var best =
            candidates.Max(item => item.Tier);

        var matches =
            candidates
                .Where(item => item.Tier == best)
                .Select(item => item.Tenant)
                .ToArray();

        return matches.Length switch
        {
            1 => new Detection(matches[0].Id.Trim(), null),
            _ => new Detection(
                null,
                $"{matches.Length} customer tenants match a partner " +
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
}
