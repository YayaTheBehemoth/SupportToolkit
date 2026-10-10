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
/// source report. The shared ticketing capability remains unaware of BackupAggregator
/// domain models.
/// </summary>
public sealed class BackupReviewTicketDraftFactory
{
    public TicketDraft Create(
        AggregatedBackupReport report)
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
                    report
                ),

            Body =
                BuildBody(
                    report
                )
        };
    }

    private static void ValidateReport(
        AggregatedBackupReport report)
    {
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
        AggregatedBackupReport report)
    {
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
        AggregatedBackupReport report)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "SUPPORTTOOLKIT // BACKUP REVIEW"
        );

        builder.AppendLine();

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
            value)
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