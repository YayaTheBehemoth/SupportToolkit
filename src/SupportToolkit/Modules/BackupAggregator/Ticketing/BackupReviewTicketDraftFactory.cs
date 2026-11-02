
using System.Text;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Core.Ticketing.Models;

namespace SupportToolkit.Modules.BackupAggregator.Ticketing;

/// <summary>
/// Translates a completed BackupAggregator report into the provider-independent
/// shared ticket model owned by Core/Ticketing.
///
/// BackupAggregator owns this translation because it owns the meaning of the
/// source report. The shared ticketing capability remains unaware of
/// BackupAggregator domain models.
/// </summary>
public sealed class BackupReviewTicketDraftFactory
{
    /// <param name="scopeNote">
    /// Optional line saying what the review covered, for reviews
    /// limited to a subset of tenants. Omitted for a full review.
    /// </param>
    public TicketDraft Create(
        AggregatedBackupReport report,
        string? scopeNote = null,
        IReadOnlyList<string>? scopeTenantLines = null,
        IReadOnlyList<string>? strayLines = null,
        string? scopeWarning = null)
    {
        ArgumentNullException.ThrowIfNull(
            report
        );

        ValidateReport(
            report
        );

        return new TicketDraft
        {
            Subject =
                BuildSubject(
                    report,
                    scopeWarning
                ),

            Body =
                BuildBody(
                    report,
                    scopeNote,
                    scopeTenantLines,
                    strayLines,
                    scopeWarning
                )
        };
    }

    private static void ValidateReport(
        AggregatedBackupReport report)
    {
        if (report.TenantsAttempted == 0)
        {
            throw new InvalidOperationException(
                "Cannot create a backup review ticket because " +
                "no customer tenants were discovered or reviewed."
            );
        }

        if (!report.IsFullyAccountedFor)
        {
            throw new InvalidOperationException(
                "Cannot create a backup review ticket because " +
                "the report does not account for every backup resource."
            );
        }

        if (!report.IsTenantReviewComplete)
        {
            throw new InvalidOperationException(
                "Cannot create a backup review ticket because " +
                "one or more tenants were not reviewed completely."
            );
        }
    }

    private static string BuildSubject(
        AggregatedBackupReport report,
        string? scopeWarning)
    {
        var prefix =
            string.IsNullOrWhiteSpace(scopeWarning)
                ? string.Empty
                : "[INCOMPLETE SCOPE] ";

        return prefix + BuildSubjectCore(report);
    }

    private static string BuildSubjectCore(
        AggregatedBackupReport report)
    {
        if (report.FindingsCount == 0
            && report.UnknownRowsCount == 0
            && report.InProgressWait is { PendingResources: > 0 })
        {
            return
                "Backup review - backups still in progress";
        }

        if (report.FindingsCount == 0
            && report.UnknownRowsCount == 0)
        {
            return
                "Backup review complete - no attention required";
        }

        var tenantCount =
            report.TenantsRequiringReview;

        return tenantCount == 1
            ? "Backup review - 1 tenant requires attention"
            : $"Backup review - {tenantCount} tenants require attention";
    }

    private static string BuildBody(
        AggregatedBackupReport report,
        string? scopeNote,
        IReadOnlyList<string>? scopeTenantLines,
        IReadOnlyList<string>? strayLines,
        string? scopeWarning)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "SUPPORTTOOLKIT // BACKUP REVIEW"
        );

        if (!string.IsNullOrWhiteSpace(scopeWarning))
        {
            builder.AppendLine(
                "!!! " + Inline(scopeWarning) + " !!!"
            );

            builder.AppendLine();
        }

        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(scopeNote))
        {
            builder.AppendLine(
                "SCOPE"
            );

            builder.AppendLine(
                Inline(
                    scopeNote
                )
            );

            foreach (var line in scopeTenantLines ?? [])
            {
                builder.AppendLine(
                    "- " + Inline(line)
                );
            }

            builder.AppendLine();
        }

        if (strayLines is { Count: > 0 })
        {
            builder.AppendLine(
                "STRAY TENANT CHECK"
            );

            foreach (var line in strayLines)
            {
                builder.AppendLine(
                    Inline(line)
                );
            }

            builder.AppendLine();
        }

        AppendSummary(
            builder,
            report
        );

        AppendAttentionSection(
            builder,
            report
        );

        AppendUnclassifiedSection(
            builder,
            report
        );

        AppendFooter(
            builder,
            report
        );

        return builder
            .ToString()
            .TrimEnd();
    }

    private static void AppendSummary(
        StringBuilder builder,
        AggregatedBackupReport report)
    {
        builder.AppendLine(
            "SUMMARY"
        );

        builder.AppendLine(
            $"Tenants reviewed: {report.TenantCount}/{report.TenantsAttempted}"
        );

        builder.AppendLine(
            $"Backup resources checked: {report.RowsChecked}"
        );

        builder.AppendLine(
            $"Healthy: {report.HealthyRowsSuppressed}"
        );

        builder.AppendLine(
            $"Require attention: {report.FindingsCount}"
        );

        builder.AppendLine(
            $"Unclassified: {report.UnknownRowsCount}"
        );

        if (report.InProgressWait is { PendingResources: > 0 } wait)
        {
            builder.AppendLine(
                $"Still in progress after waiting: " +
                $"{wait.PendingResources} resource(s) in " +
                $"{wait.PendingTenants} tenant(s). Their result is " +
                "not final."
            );
        }

        builder.AppendLine();
    }

    private static void AppendAttentionSection(
        StringBuilder builder,
        AggregatedBackupReport report)
    {
        var tenants =
            report.Tenants
                .Where(
                    tenant =>
                        tenant.FindingsCount > 0
                )
                .OrderBy(
                    tenant =>
                        tenant.TenantName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (tenants.Count == 0)
        {
            return;
        }

        builder.AppendLine(
            "ATTENTION REQUIRED"
        );

        builder.AppendLine();

        foreach (var tenant in tenants)
        {
            builder.AppendLine(
                Inline(
                    tenant.TenantName
                )
            );

            foreach (var entry in tenant.Findings
                         .OrderBy(
                             entry =>
                                 entry.ResourceName,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                builder.AppendLine(
                    BuildFindingLine(
                        entry
                    )
                );
            }

            builder.AppendLine();
        }
    }

    private static void AppendUnclassifiedSection(
        StringBuilder builder,
        AggregatedBackupReport report)
    {
        var tenants =
            report.Tenants
                .Where(
                    tenant =>
                        tenant.UnknownRowsCount > 0
                )
                .OrderBy(
                    tenant =>
                        tenant.TenantName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (tenants.Count == 0)
        {
            return;
        }

        builder.AppendLine(
            "UNCLASSIFIED DATA"
        );

        builder.AppendLine(
            "These resources were received but could not be classified safely."
        );

        builder.AppendLine();

        foreach (var tenant in tenants)
        {
            builder.AppendLine(
                Inline(
                    tenant.TenantName
                )
            );

            foreach (var entry in tenant.UnknownRows
                         .OrderBy(
                             entry =>
                                 entry.ResourceName,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                builder.AppendLine(
                    BuildUnclassifiedLine(
                        entry
                    )
                );
            }

            builder.AppendLine();
        }
    }

    private static void AppendFooter(
        StringBuilder builder,
        AggregatedBackupReport report)
    {
        if (report.FindingsCount == 0
            && report.UnknownRowsCount == 0)
        {
            builder.AppendLine(
                "No backup resources require attention."
            );
        }

        var resourceWord =
            report.HealthyRowsSuppressed == 1
                ? "resource"
                : "resources";

        builder.AppendLine(
            $"{report.HealthyRowsSuppressed} healthy backup {resourceWord} " +
            "were checked and suppressed from ticket details."
        );
    }

    private static string BuildFindingLine(
        BackupReportEntry entry)
    {
        return
            $"- {Inline(entry.ResourceName)}" +
            $" | Status: {Inline(BackupReportEntryPresentation.GetDisplayStatus(entry))}" +
            $" | Plan: {InlineOrDash(entry.PlanName)}" +
            $" | Last activity (UTC): " +
            $"{BackupReportEntryPresentation.FormatTimestamp(entry.LastBackupRun)}" +
            $" | Last success (UTC): " +
            $"{BackupReportEntryPresentation.FormatTimestamp(entry.LastSuccessfulBackup)}";
    }

    private static string BuildUnclassifiedLine(
        BackupReportEntry entry)
    {
        return
            $"- {Inline(entry.ResourceName)}" +
            $" | Reason: {Inline(BackupReportEntryPresentation.GetUnclassifiedReason(entry))}" +
            $" | Plan: {InlineOrDash(entry.PlanName)}" +
            $" | Last activity (UTC): " +
            $"{BackupReportEntryPresentation.FormatTimestamp(entry.LastBackupRun)}" +
            $" | Last success (UTC): " +
            $"{BackupReportEntryPresentation.FormatTimestamp(entry.LastSuccessfulBackup)}";
    }

    private static string InlineOrDash(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
            value
        )
            ? "-"
            : Inline(
                value
            );
    }

    private static string Inline(
        string value)
    {
        return value
            .Replace(
                '\r',
                ' '
            )
            .Replace(
                '\n',
                ' '
            )
            .Trim();
    }
}
