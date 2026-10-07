using System.Globalization;
using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

public sealed class ConsoleBackupAggregatorReporter
{
    private const int ResourceWidth =
        30;

    private const int StatusWidth =
        24;

    private const int PlanWidth =
        26;

    private const int TimeWidth =
        19;

    private const int ReasonWidth =
        28;

    public void Write(
        AggregatedBackupReport report)
    {
        WriteTitle();

        WriteSummary(
            report
        );

        if (!report.IsFullyAccountedFor)
        {
            WriteAccountingFailure(
                report
            );

            return;
        }

        WriteAttentionSection(
            report
        );

        WriteUnclassifiedSection(
            report
        );

        WriteFooter(
            report
        );
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

        foreach (var tenant
                 in tenants)
        {
            Console.WriteLine(
                tenant.TenantName
            );

            Console.WriteLine(
                new string(
                    '-',
                    Math.Min(
                        tenant.TenantName.Length,
                        80
                    )
                )
            );

            Console.WriteLine();

            WriteAttentionHeader();

            foreach (var entry
                     in tenant.Findings)
            {
                WriteAttentionRow(
                    entry
                );
            }

            Console.WriteLine();
        }
    }

    private static void WriteAttentionHeader()
    {
        Console.WriteLine(
            $"{Fit("Resource", ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{Fit("Status", StatusWidth).PadRight(StatusWidth)}  " +
            $"{Fit("Plan", PlanWidth).PadRight(PlanWidth)}  " +
            $"{Fit("Last activity (UTC)", TimeWidth)}"
        );

        Console.WriteLine(
            $"{new string('-', ResourceWidth)}  " +
            $"{new string('-', StatusWidth)}  " +
            $"{new string('-', PlanWidth)}  " +
            $"{new string('-', TimeWidth)}"
        );
    }

    private static void WriteAttentionRow(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"{Fit(entry.DeviceName, ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{Fit(GetDisplayStatus(entry), StatusWidth).PadRight(StatusWidth)}  " +
            $"{Fit(entry.PlanName ?? "-", PlanWidth).PadRight(PlanWidth)}  " +
            $"{Fit(FormatTimestamp(entry.LastBackupRun), TimeWidth)}"
        );
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
            "These activities were received but could not be " +
            "classified safely."
        );

        Console.WriteLine();

        foreach (var tenant
                 in tenants)
        {
            Console.WriteLine(
                tenant.TenantName
            );

            Console.WriteLine(
                new string(
                    '-',
                    Math.Min(
                        tenant.TenantName.Length,
                        80
                    )
                )
            );

            Console.WriteLine();

            WriteUnclassifiedHeader();

            foreach (var entry
                     in tenant.UnknownRows)
            {
                WriteUnclassifiedRow(
                    entry
                );
            }

            Console.WriteLine();
        }
    }

    private static void WriteUnclassifiedHeader()
    {
        Console.WriteLine(
            $"{Fit("Resource", ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{Fit("Reason", ReasonWidth).PadRight(ReasonWidth)}  " +
            $"{Fit("Plan", PlanWidth).PadRight(PlanWidth)}  " +
            $"{Fit("Last activity (UTC)", TimeWidth)}"
        );

        Console.WriteLine(
            $"{new string('-', ResourceWidth)}  " +
            $"{new string('-', ReasonWidth)}  " +
            $"{new string('-', PlanWidth)}  " +
            $"{new string('-', TimeWidth)}"
        );
    }

    private static void WriteUnclassifiedRow(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"{Fit(entry.DeviceName, ResourceWidth).PadRight(ResourceWidth)}  " +
            $"{Fit(GetUnclassifiedReason(entry), ReasonWidth).PadRight(ReasonWidth)}  " +
            $"{Fit(entry.PlanName ?? "-", PlanWidth).PadRight(PlanWidth)}  " +
            $"{Fit(FormatTimestamp(entry.LastBackupRun), TimeWidth)}"
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
            "The aggregation pipeline did not account for every " +
            "normalized resource."
        );

        Console.WriteLine(
            $"Expected:  {report.RowsChecked}"
        );

        Console.WriteLine(
            $"Accounted: {report.AccountedRows}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Detailed output has been stopped because the result " +
            "cannot be trusted."
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

        Console.WriteLine(
            $"{report.HealthyRowsSuppressed} healthy backup " +
            $"{Pluralize(
                report.HealthyRowsSuppressed,
                "resource",
                "resources"
            )} " +
            "were checked and intentionally suppressed from details."
        );

        Console.WriteLine();
    }

    private static string GetDisplayStatus(
        BackupReportEntry entry)
    {
        var state =
            entry.DeviceState;

        var result =
            entry.LastResult;

        if (string.Equals(
                state,
                "completed",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            if (string.Equals(
                    result,
                    "ok",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Successful";
            }

            if (string.Equals(
                    result,
                    "warning",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Completed with warnings";
            }

            if (IsMissing(
                    result))
            {
                return "Completed - result unknown";
            }

            return
                $"Completed - {ToDisplayText(result)}";
        }

        if (string.Equals(
                state,
                "running",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "In progress";
        }

        if (string.Equals(
                state,
                "waiting",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Waiting";
        }

        return ToDisplayText(
            state ?? "Unknown"
        );
    }

    private static string GetUnclassifiedReason(
        BackupReportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(
                entry.DeviceName)
            || string.Equals(
                entry.DeviceName,
                "<unknown resource>",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Resource identity missing";
        }

        if (entry.LastBackupRun is null)
        {
            return "Activity timestamp missing";
        }

        if (string.IsNullOrWhiteSpace(
                entry.DeviceState))
        {
            return "Activity state missing";
        }

        if (string.Equals(
                entry.DeviceState,
                "completed",
                StringComparison.OrdinalIgnoreCase
            )
            && IsMissing(
                entry.LastResult))
        {
            return "Result code missing";
        }

        return "Activity data not recognized";
    }

    private static bool IsMissing(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                   value
               )
               || string.Equals(
                   value,
                   "<missing>",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static string ToDisplayText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return "Unknown";
        }

        var normalized =
            value
                .Replace(
                    '_',
                    ' '
                )
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return "Unknown";
        }

        return char.ToUpper(
                   normalized[0],
                   CultureInfo.InvariantCulture
               )
               + normalized[1..];
    }

    private static string FormatTimestamp(
        DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "-"
            : timestamp.Value
                .ToUniversalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture
                );
    }

    private static string Fit(
        string value,
        int width)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return "-";
        }

        if (value.Length <= width)
        {
            return value;
        }

        if (width <= 3)
        {
            return value[..width];
        }

        return
            value[..(width - 3)]
            + "...";
    }

    private static string Pluralize(
        int count,
        string singular,
        string plural)
    {
        return count == 1
            ? singular
            : plural;
    }
}