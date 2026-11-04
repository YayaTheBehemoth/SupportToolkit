using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Units;

namespace SupportToolkit.Modules.UsageReport.Models;

/// <summary>
/// One Acronis storage figure for a customer, in bytes as Acronis
/// reported it, or unknown with the reason. Never zero for unknown.
/// </summary>
public sealed record UsageMeasurement(
    decimal? Bytes,
    string? Problem,
    bool NotReported = false)
{
    public const decimal BytesPerGb = StorageUnits.BytesPerGb;
    public const decimal BytesPerPortalGb = StorageUnits.BytesPerGib;

    public static UsageMeasurement Unknown(string problem) =>
        new(null, problem);

    /// <summary>
    /// Acronis has reported nothing yet for the tenant or the metric,
    /// as opposed to reporting something unusable (a missing value, an
    /// unexpected unit).
    /// </summary>
    public static UsageMeasurement NothingReported(string problem) =>
        new(null, problem, NotReported: true);

    /// <summary>
    /// Several tenants' figures added up. Any unknown part makes the sum
    /// unknown, with that part's reason.
    /// </summary>
    public static UsageMeasurement Sum(IReadOnlyList<UsageMeasurement> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (parts.Count == 0)
        {
            return Unknown("no tenant");
        }

        var unknown = parts.FirstOrDefault(part => !part.IsKnown);

        return unknown ?? new UsageMeasurement(parts.Sum(part => part.Bytes!.Value), null);
    }

    public bool IsKnown => Bytes is not null;

    /// <summary>
    /// Decimal gigabytes (10^9 bytes), the unit Business Central bills
    /// in. This is the figure that is compared.
    /// </summary>
    public decimal? Gb => Bytes / BytesPerGb;

    /// <summary>
    /// The figure the Acronis portal shows. The portal labels it GB but
    /// counts 1,073,741,824 bytes per GB, so it is about 7% lower than
    /// <see cref="Gb"/> for the same data.
    /// </summary>
    public decimal? PortalGb => Bytes / BytesPerPortalGb;
}

/// <summary>
/// A Business Central subscription line that is billed, as BC states it.
/// The amount is BC's own figure; the toolkit does not recalculate it.
/// </summary>
public sealed record UsageBillingLine(
    string SubscriptionNumber,
    int? LineNumber,
    string ItemNumber,
    string? Description,
    string? UnitOfMeasure,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? LineDiscountPercent,
    decimal? Amount);

public enum UsageRowKind
{
    /// <summary>
    /// A Business Central customer with active backup lines: the report
    /// itself.
    /// </summary>
    Customer,

    /// <summary>
    /// Set up on both sides (an active backup line and a matched Acronis
    /// tenant), but Acronis has reported no usage for it yet. Listed
    /// separately so it can be raised with whoever set it up.
    /// </summary>
    AwaitingUsage,

    /// <summary>
    /// A customer whose backup lines are all on a subscription that is not
    /// active (or are omitted). Left out of the report and listed apart,
    /// with any Acronis storage still shown.
    /// </summary>
    NotActive,

    /// <summary>
    /// An Acronis customer tenant with storage but no matched customer
    /// that has a backup subscription line: usage that may not be billed.
    /// </summary>
    TenantWithoutSubscription,

    /// <summary>
    /// A tenant that fits nowhere else and is worth knowing about, such as
    /// one with no matched subscription and no usage reported, or no
    /// storage at all. Listed apart; the row's remark says which case.
    /// </summary>
    SpecialCase
}

/// <summary>
/// One line of the report.
///
/// Problems make the row unknown: something needed for the comparison is
/// missing or contradictory, and the row says what. Remarks are worth a
/// look but do not stop the comparison.
///
/// Difference is billed quantity minus Acronis total, both in decimal GB.
/// A negative number means less is billed than Acronis reports. Nothing
/// is judged: there are no thresholds.
/// </summary>
public sealed record UsageReportRow(
    UsageRowKind Kind,
    string? CustomerNumber,
    string? CustomerName,
    IReadOnlyList<string> TenantIds,
    IReadOnlyList<string> TenantNames,
    IReadOnlyList<UsageBillingLine> Lines,
    int ExcludedLines,
    decimal? BilledQuantity,
    decimal? BilledAmount,
    UsageMeasurement Total,
    UsageMeasurement Backup,
    UsageMeasurement Microsoft365,
    decimal? DifferenceGb,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Remarks)
{
    /// <summary>Displayed name: the customer's, else the tenant's.</summary>
    public string Name =>
        !string.IsNullOrWhiteSpace(CustomerName)
            ? CustomerName!
            : TenantNames.Count > 0
                ? string.Join(", ", TenantNames)
                : CustomerNumber ?? "(unnamed)";

    public bool IsKnown => Problems.Count == 0;

    public bool IsCompared =>
        Kind == UsageRowKind.Customer
        && IsKnown
        && DifferenceGb is not null;
}

/// <summary>
/// A usage report: what Acronis reports next to what Business Central
/// bills, per customer. It holds no rule of any user interface and is
/// not stored anywhere.
/// </summary>
public sealed record UsageReport(
    DateTimeOffset GeneratedAt,
    SupportToolkitMode Mode,
    IReadOnlyList<UsageReportRow> Rows,
    int PendingMatches,
    int UnattributedLines,
    string? UsagePeriodStart,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Problems)
{
    public IEnumerable<UsageReportRow> Customers =>
        Rows.Where(row => row.Kind == UsageRowKind.Customer);

    public IEnumerable<UsageReportRow> AwaitingUsage =>
        Rows.Where(row => row.Kind == UsageRowKind.AwaitingUsage);

    public IEnumerable<UsageReportRow> NotActive =>
        Rows.Where(row => row.Kind == UsageRowKind.NotActive);

    public IEnumerable<UsageReportRow> TenantsWithoutSubscription =>
        Rows.Where(row => row.Kind == UsageRowKind.TenantWithoutSubscription);

    public IEnumerable<UsageReportRow> SpecialCases =>
        Rows.Where(row => row.Kind == UsageRowKind.SpecialCase);

    public int CustomersCompared =>
        Customers.Count(row => row.IsCompared);

    public int CustomersWithDifference =>
        Customers.Count(row => row.IsCompared && row.DifferenceGb != 0m);

    public int CustomersUnknown =>
        Customers.Count(row => !row.IsKnown);

    /// <summary>
    /// True only when every customer in the report was compared, every
    /// listed tenant's usage is known, no match is waiting for review and
    /// no read was partial.
    ///
    /// Customers without usage yet and customers that are not active are
    /// listed apart and do not make the report incomplete; an interface
    /// must still show how many there are, never present them as compared.
    /// </summary>
    public bool IsComplete =>
        Problems.Count == 0
        && PendingMatches == 0
        && UnattributedLines == 0
        && Rows
            .Where(row => row.Kind
                is UsageRowKind.Customer
                or UsageRowKind.TenantWithoutSubscription)
            .All(row => row.IsKnown);
}
