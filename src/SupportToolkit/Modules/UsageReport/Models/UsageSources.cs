using SupportToolkit.Domain.CustomerMapping.Matching;

namespace SupportToolkit.Modules.UsageReport.Models;

/// <summary>
/// One subscription line as the billing system states it. Every value is
/// nullable: missing is unknown, never zero.
/// </summary>
public sealed record SubscriptionBillingLine(
    string? SubscriptionNumber,
    int? LineNumber,
    string? ItemNumber,
    string? Description,
    string? UnitOfMeasure,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? LineDiscountPercent,
    decimal? Amount,
    bool? OmitLine);

/// <summary>
/// The backup system's customer tenants (the operator's own tenant and
/// deleted tenants already left out), with anything worth saying about
/// how they were chosen.
/// </summary>
public sealed record UsageTenants(
    IReadOnlyList<CustomerParty> Tenants,
    string? Notice);

/// <summary>
/// The three storage figures for one tenant, as the backup system's
/// portal shows them.
/// </summary>
public sealed record TenantStorage(
    UsageMeasurement Total,
    UsageMeasurement Backup,
    UsageMeasurement Microsoft365)
{
    public static TenantStorage NothingReported(string problem) =>
        new(
            UsageMeasurement.NothingReported(problem),
            UsageMeasurement.NothingReported(problem),
            UsageMeasurement.NothingReported(problem));
}

/// <summary>
/// Storage per requested tenant. A tenant with no entry had nothing
/// reported: unknown, not zero.
/// </summary>
public sealed record UsageStorage(
    IReadOnlyDictionary<string, TenantStorage> ByTenant,
    string? PeriodStart,
    IReadOnlyList<string> Notices);
