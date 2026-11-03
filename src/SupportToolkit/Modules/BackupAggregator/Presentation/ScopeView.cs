using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Presentation;

public sealed record ScopeTenantView(
    string Name,
    string TenantId,
    string? CustomerNumber,
    string? Detail,
    bool IsOperatorTenant);

public sealed record ScopeExceptionView(
    string Subject,
    IReadOnlyList<string> Reasons);

/// <summary>
/// What a review decided about tenants, partitioned exactly as the report
/// partitions them: in scope + excluded + needs review + unmatched.
/// </summary>
public sealed record ScopeView(
    IReadOnlyList<ScopeTenantView> InScope,
    IReadOnlyList<ScopeTenantView> Excluded,
    IReadOnlyList<ScopeTenantView> NeedsReview,
    IReadOnlyList<ScopeTenantView> Unmatched,
    IReadOnlyList<ScopeTenantView> LeakCandidates,
    bool LeakCheckAvailable,
    bool ReconciliationRun,
    IReadOnlyList<string> Discrepancies,
    IReadOnlyList<ScopeExceptionView> Exceptions,
    IReadOnlyList<string> Notices)
{
    public static ScopeView? From(
        ScopedBackupInventoryReviewResult review)
    {
        ArgumentNullException.ThrowIfNull(review);

        var report = review.ScopeReport;

        if (report is null)
        {
            return null;
        }

        static IOrderedEnumerable<T> Sorted<T>(
            IEnumerable<T> source,
            Func<T, string> name,
            Func<T, string> id) =>
            source
                .OrderBy(name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(id, StringComparer.OrdinalIgnoreCase);

        var stray = report.StrayTenants;

        return new ScopeView(
            Sorted(report.InScope, t => t.TenantName, t => t.TenantId)
                .Select(t => new ScopeTenantView(
                    t.TenantName, t.TenantId, t.CustomerNumber, null,
                    t.IsOperatorTenant))
                .ToArray(),
            Sorted(report.Excluded, t => t.TenantName, t => t.TenantId)
                .Select(t => new ScopeTenantView(
                    t.TenantName, t.TenantId, t.CustomerNumber, t.Detail,
                    false))
                .ToArray(),
            Sorted(report.NeedsReview, t => t.TenantName, t => t.TenantId)
                .Select(t => new ScopeTenantView(
                    t.TenantName, t.TenantId, t.CustomerNumber, t.Reason,
                    false))
                .ToArray(),
            Sorted(report.UnscopedTenants, t => t.TenantName, t => t.TenantId)
                .Select(t => new ScopeTenantView(
                    t.TenantName, t.TenantId, t.CustomerNumber, t.Reason,
                    false))
                .ToArray(),
            (stray?.LeakCandidates ?? [])
                .Select(t => new ScopeTenantView(
                    t.TenantName, t.TenantId, t.CustomerNumber, t.Reason,
                    false))
                .ToArray(),
            stray?.LeakCheckAvailable ?? false,
            report.ReconciliationRun,
            report.Discrepancies
                .Select(d => $"{d.TenantName}: {d.Discrepancy}")
                .ToArray(),
            report.Ambiguous
                .Concat(report.UnscopedRecords)
                .Select(e => new ScopeExceptionView(e.Subject, e.Reasons))
                .ToArray(),
            report.Notices);
    }
}

/// <summary>
/// What a ticket may do with this review. Mirrors the rules on
/// ScopedBackupInventoryReviewResult; nothing is decided here.
/// </summary>
public enum TicketOutcome
{
    /// <summary>The review is complete.</summary>
    Allowed,

    /// <summary>
    /// Only tenants that could not be matched are missing: the ticket
    /// carries an INCOMPLETE SCOPE banner.
    /// </summary>
    AllowedWithIncompleteScopeBanner,

    /// <summary>A ticket must not be created from this review.</summary>
    Blocked
}

public sealed record CompletenessView(
    bool InventoryComplete,
    bool ScopeComplete,
    bool OverallComplete,
    TicketOutcome TicketOutcome,
    IReadOnlyList<string> Blockers)
{
    public static CompletenessView From(
        ScopedBackupInventoryReviewResult review)
    {
        ArgumentNullException.ThrowIfNull(review);

        var blockers = review.TicketBlockers;

        var outcome =
            review.IsOverallComplete
                ? TicketOutcome.Allowed
                : blockers.Count == 0
                    ? TicketOutcome.AllowedWithIncompleteScopeBanner
                    : TicketOutcome.Blocked;

        return new CompletenessView(
            review.IsInventoryComplete,
            review.IsScopeComplete,
            review.IsOverallComplete,
            outcome,
            blockers);
    }
}
