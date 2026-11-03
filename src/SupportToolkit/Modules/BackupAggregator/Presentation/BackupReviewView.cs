using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;

namespace SupportToolkit.Modules.BackupAggregator.Presentation;

/// <summary>
/// One backup resource as an interface shows it. Wording comes from
/// BackupReportEntryPresentation, the same source the console uses, so the
/// console and the web cannot describe a resource differently.
/// </summary>
public sealed record BackupEntryView(
    string Resource,
    string Status,
    string? Plan,
    string? UnclassifiedReason,
    DateTimeOffset? LastActivity,
    bool IsInProgress)
{
    public static BackupEntryView From(
        BackupReportEntry entry,
        bool unclassified) =>
        new(
            entry.ResourceName,
            BackupReportEntryPresentation.GetDisplayStatus(entry),
            entry.PlanName,
            unclassified
                ? BackupReportEntryPresentation.GetUnclassifiedReason(entry)
                : null,
            entry.LastBackupRun,
            entry.IsInProgress);
}

public sealed record TenantEntriesView(
    string Tenant,
    IReadOnlyList<BackupEntryView> Entries);

public sealed record TenantFailureView(
    string Tenant,
    string Stage,
    string Reason);

/// <summary>
/// How trustworthy the inventory is, as a fact rather than a sentence.
/// </summary>
public enum InventoryTrust
{
    /// <summary>Every attempted tenant was reviewed and every resource accounted for.</summary>
    Complete,

    /// <summary>Some tenants could not be reviewed: a partial review.</summary>
    PartialTenants,

    /// <summary>
    /// Resources checked do not equal resources accounted for. Details are
    /// withheld because the result cannot be trusted.
    /// </summary>
    AccountingError
}

/// <summary>
/// The inventory of one review, split the way the console report splits it.
/// Nothing is derived that the report does not already say; missing or
/// contradictory data stays visible as unclassified, incomplete or an
/// accounting error and is never turned into "healthy".
/// </summary>
public sealed record BackupReviewView(
    int TenantsAttempted,
    int TenantsReviewed,
    int TenantsFailed,
    int TenantsRequiringAttention,
    int ResourcesChecked,
    int ResourcesAccounted,
    int Healthy,
    int RequireAttention,
    int Unclassified,
    int InProgressPending,
    InventoryTrust Trust,
    IReadOnlyList<TenantEntriesView> Attention,
    IReadOnlyList<TenantEntriesView> UnclassifiedTenants,
    IReadOnlyList<TenantFailureView> Incomplete)
{
    public bool NothingReviewed => ResourcesChecked == 0;

    public static BackupReviewView From(
        AggregatedBackupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var trust =
            !report.IsFullyAccountedFor
                ? InventoryTrust.AccountingError
                : report.IsTenantReviewComplete
                    ? InventoryTrust.Complete
                    : InventoryTrust.PartialTenants;

        var incomplete =
            report.Failures
                .OrderBy(
                    failure => failure.TenantName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    failure => new TenantFailureView(
                        failure.TenantName,
                        failure.Stage,
                        failure.Reason))
                .ToArray();

        // The console stops detail output when accounting fails.
        var detailsTrusted = trust != InventoryTrust.AccountingError;

        return new BackupReviewView(
            report.TenantsAttempted,
            report.TenantCount,
            report.FailedTenantCount,
            report.TenantsRequiringReview,
            report.RowsChecked,
            report.AccountedRows,
            report.HealthyRowsSuppressed,
            report.FindingsCount,
            report.UnknownRowsCount,
            report.InProgressPending,
            trust,
            detailsTrusted
                ? Group(
                    report,
                    tenant => tenant.FindingsCount > 0,
                    tenant => tenant.Findings,
                    unclassified: false)
                : [],
            detailsTrusted
                ? Group(
                    report,
                    tenant => tenant.UnknownRowsCount > 0,
                    tenant => tenant.UnknownRows,
                    unclassified: true)
                : [],
            incomplete);
    }

    private static TenantEntriesView[] Group(
        AggregatedBackupReport report,
        Func<TenantBackupReport, bool> include,
        Func<TenantBackupReport, IReadOnlyList<BackupReportEntry>> entries,
        bool unclassified) =>
        report.Tenants
            .Where(include)
            .OrderBy(
                tenant => tenant.TenantName,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                tenant => new TenantEntriesView(
                    tenant.TenantName,
                    entries(tenant)
                        .Select(
                            entry => BackupEntryView.From(
                                entry,
                                unclassified))
                        .ToArray()))
            .ToArray();
}
