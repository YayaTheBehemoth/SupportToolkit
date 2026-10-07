using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

public sealed class ConsoleBackupAggregatorReporter
{
    public void Write(
        TenantBackupReport report,
        int rawActivityCount)
    {
        Console.WriteLine();

        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine(
            "================="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Tenant:                {report.TenantName}"
        );

        Console.WriteLine(
            $"Raw activities:        {rawActivityCount}"
        );

        Console.WriteLine(
            $"Resources checked:     {report.RowsChecked}"
        );

        Console.WriteLine(
            $"Completed suppressed:  {report.HealthyRowsSuppressed}"
        );

        Console.WriteLine(
            $"Needs review:           {report.FindingsCount}"
        );

        Console.WriteLine(
            $"Unknown:                {report.UnknownRowsCount}"
        );

        var accounted =
            report.HealthyRowsSuppressed
            + report.FindingsCount
            + report.UnknownRowsCount;

        Console.WriteLine(
            $"Accounted:              {accounted}/{report.RowsChecked}"
        );

        Console.WriteLine();

        if (accounted
            != report.RowsChecked)
        {
            Console.WriteLine(
                "!!! ACCOUNTING FAILURE !!!"
            );

            Console.WriteLine();

            Console.WriteLine(
                "Not every normalized resource received a classification."
            );

            Console.WriteLine();

            return;
        }

        WriteFindings(
            report.Findings
        );

        WriteUnknownRows(
            report.UnknownRows
        );

        if (!report.RequiresReview)
        {
            Console.WriteLine(
                "No backup resources require review."
            );

            Console.WriteLine();
        }
    }

    private static void WriteFindings(
        IReadOnlyList<BackupReportEntry> findings)
    {
        if (findings.Count == 0)
        {
            return;
        }

        Console.WriteLine(
            "NEEDS REVIEW"
        );

        Console.WriteLine(
            "============"
        );

        Console.WriteLine();

        foreach (var entry
                 in findings)
        {
            WriteEntry(
                entry
            );
        }
    }

    private static void WriteUnknownRows(
        IReadOnlyList<BackupReportEntry> unknownRows)
    {
        if (unknownRows.Count == 0)
        {
            return;
        }

        Console.WriteLine(
            "UNKNOWN"
        );

        Console.WriteLine(
            "======="
        );

        Console.WriteLine();

        foreach (var entry
                 in unknownRows)
        {
            WriteEntry(
                entry
            );
        }
    }

    private static void WriteEntry(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"Resource: {entry.DeviceName}"
        );

        Console.WriteLine(
            $"  State:  {entry.DeviceState ?? "<missing>"}"
        );

        Console.WriteLine(
            $"  Result: {entry.LastResult}"
        );

        Console.WriteLine(
            $"  Plan:   {entry.PlanName ?? "<missing>"}"
        );

        Console.WriteLine(
            $"  Time:   {FormatTimestamp(entry.LastBackupRun)}"
        );

        Console.WriteLine();
    }

    private static string FormatTimestamp(
        DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "<missing>"
            : timestamp.Value.ToString(
                "O"
            );
    }
}