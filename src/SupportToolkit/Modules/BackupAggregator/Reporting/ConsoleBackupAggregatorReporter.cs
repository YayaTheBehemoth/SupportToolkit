using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

public sealed class ConsoleBackupAggregatorReporter
{
    private const int ResourceWidth = 30;
    private const int StatusWidth = 24;
    private const int PlanWidth = 26;
    private const int TimeWidth = 19;
    private const int ReasonWidth = 28;

    public void Write(
        AggregatedBackupReport report)
    {
        WriteTitle();
        WriteSummary(report);

        if (!report.IsFullyAccountedFor)
        {
            WriteAccountingFailure(report);
            return;
        }

        WriteAttentionSection(report);
        WriteUnclassifiedSection(report);
        WriteFooter(report);
    }

    private static void WriteTitle()
    {
        Console.WriteLine();
        Console.WriteLine(
            "SUPPORTTOOLKIT // BACKUP REVIEW"
        );
        Console.WriteLine(
            "==============================="
        );
        Console.WriteLine();
    }

    private static void WriteSummary(
        AggregatedBackupReport report)
    {
        Console.WriteLine(
            "SUMMARY"
        );
        Console.WriteLine(
            "-------"
        );

        WriteSummaryRow(
            "Tenants checked",
            report.TenantCount
        );
        WriteSummaryRow(
            "Tenants requiring attention",
            report.TenantsRequiringReview
        );
        WriteSummaryRow(
            "Backup resources checked",
            report.RowsChecked
        );
        WriteSummaryRow(
            "Healthy",
            report.HealthyRowsSuppressed
        );
        WriteSummaryRow(
            "Require attention",
            report.FindingsCount
        );
        WriteSummaryRow(
            "Unclassified",
            report.UnknownRowsCount
        );

        Console.WriteLine(
            $"{"Coverage",-30}" +
            $"{report.AccountedRows}/{report.RowsChecked}"
        );
        Console.WriteLine();
    }

    private static void WriteSummaryRow(
        string label,
        int value)
    {
        Console.WriteLine(
            $"{label,-30}{value}"
        );
    }

    private static void WriteAttentionSection(
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

        Console.WriteLine(
            "ATTENTION REQUIRED"
        );
        Console.WriteLine(
            "------------------"
        );
        Console.WriteLine();

        foreach (var tenant in tenants)
        {
            WriteTenantHeading(
                tenant.TenantName
            );
            WriteAttentionHeader();

            foreach (var entry in tenant.Findings)
            {
                WriteAttentionRow(entry);
            }

            Console.WriteLine();
        }
    }

    private static void WriteUnclassifiedSection(
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

        Console.WriteLine(
            "UNCLASSIFIED DATA"
        );
        Console.WriteLine(
            "-----------------"
        );
        Console.WriteLine(
            "These resources were received but could not be classified safely."
        );
        Console.WriteLine();

        foreach (var tenant in tenants)
        {
            WriteTenantHeading(
                tenant.TenantName
            );
            WriteUnclassifiedHeader();

            foreach (var entry in tenant.UnknownRows)
            {
                WriteUnclassifiedRow(entry);
            }

            Console.WriteLine();
        }
    }

    private static void WriteTenantHeading(
        string tenantName)
    {
        Console.WriteLine(
            tenantName
        );
        Console.WriteLine(
            new string(
                '-',
                Math.Min(
                    tenantName.Length,
                    80
                )
            )
        );
        Console.WriteLine();
    }

    private static void WriteAttentionHeader()
    {
        Console.WriteLine(
            $"{BackupReportEntryPresentation.Fit("Resource", ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Status", StatusWidth).PadRight(StatusWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Plan", PlanWidth).PadRight(PlanWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Last activity (UTC)", TimeWidth)}"
        );

        WriteTableRule(
            StatusWidth
        );
    }

    private static void WriteUnclassifiedHeader()
    {
        Console.WriteLine(
            $"{BackupReportEntryPresentation.Fit("Resource", ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Reason", ReasonWidth).PadRight(ReasonWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Plan", PlanWidth).PadRight(PlanWidth)}  " +
            $"{BackupReportEntryPresentation.Fit("Last activity (UTC)", TimeWidth)}"
        );

        WriteTableRule(
            ReasonWidth
        );
    }

    private static void WriteTableRule(
        int secondColumnWidth)
    {
        Console.WriteLine(
            $"{new string('-', ResourceWidth)}  " +
            $"{new string('-', secondColumnWidth)}  " +
            $"{new string('-', PlanWidth)}  " +
            $"{new string('-', TimeWidth)}"
        );
    }

    private static void WriteAttentionRow(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"{BackupReportEntryPresentation.Fit(entry.DeviceName, ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(BackupReportEntryPresentation.GetDisplayStatus(entry), StatusWidth).PadRight(StatusWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(entry.PlanName, PlanWidth).PadRight(PlanWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(BackupReportEntryPresentation.FormatTimestamp(entry.LastBackupRun), TimeWidth)}"
        );
    }

    private static void WriteUnclassifiedRow(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"{BackupReportEntryPresentation.Fit(entry.DeviceName, ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(BackupReportEntryPresentation.GetUnclassifiedReason(entry), ReasonWidth).PadRight(ReasonWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(entry.PlanName, PlanWidth).PadRight(PlanWidth)}  " +
            $"{BackupReportEntryPresentation.Fit(BackupReportEntryPresentation.FormatTimestamp(entry.LastBackupRun), TimeWidth)}"
        );
    }

    private static void WriteAccountingFailure(
        AggregatedBackupReport report)
    {
        Console.WriteLine(
            "ACCOUNTING ERROR"
        );
        Console.WriteLine(
            "----------------"
        );
        Console.WriteLine();
        Console.WriteLine(
            "The aggregation pipeline did not account for every normalized resource."
        );
        Console.WriteLine(
            $"Expected:  {report.RowsChecked}"
        );
        Console.WriteLine(
            $"Accounted: {report.AccountedRows}"
        );
        Console.WriteLine();
        Console.WriteLine(
            "Detailed output has been stopped because the result cannot be trusted."
        );
        Console.WriteLine();
    }

    private static void WriteFooter(
        AggregatedBackupReport report)
    {
        if (report.RowsChecked == 0)
        {
            Console.WriteLine(
                "No backup resources were available for review."
            );
            Console.WriteLine();
            return;
        }

        if (report.FindingsCount == 0
            && report.UnknownRowsCount == 0)
        {
            Console.WriteLine(
                "No backup resources require attention."
            );
            Console.WriteLine();
        }

        var resourceWord =
            report.HealthyRowsSuppressed == 1
                ? "resource"
                : "resources";

        Console.WriteLine(
            $"{report.HealthyRowsSuppressed} healthy backup {resourceWord} " +
            "were checked and intentionally suppressed from details."
        );
        Console.WriteLine();
    }
}
