using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

public sealed class ConsoleBackupAggregatorReporter
{
    public void Write(
        AggregatedBackupReport report)
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
            $"Tenants processed:       {report.TenantCount}"
        );

        Console.WriteLine(
            $"Tenants needing review:  {report.TenantsRequiringReview}"
        );

        Console.WriteLine(
            $"Resources checked:       {report.RowsChecked}"
        );

        Console.WriteLine(
            $"Completed suppressed:    {report.HealthyRowsSuppressed}"
        );

        Console.WriteLine(
            $"Needs review:             {report.FindingsCount}"
        );

        Console.WriteLine(
            $"Unknown:                  {report.UnknownRowsCount}"
        );

        Console.WriteLine(
            $"Accounted:                " +
            $"{report.AccountedRows}/{report.RowsChecked}"
        );

        Console.WriteLine();

        if (!report.IsFullyAccountedFor)
        {
            Console.WriteLine(
                "!!! ACCOUNTING FAILURE !!!"
            );

            Console.WriteLine();

            Console.WriteLine(
                "Not every normalized backup resource " +
                "received a classification."
            );

            Console.WriteLine();

            return;
        }

        var tenantsRequiringReview =
            report.Tenants
                .Where(
                    tenant =>
                        tenant.RequiresReview
                )
                .OrderBy(
                    tenant =>
                        tenant.TenantName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (tenantsRequiringReview.Count == 0)
        {
            Console.WriteLine(
                "No backup resources require review."
            );

            Console.WriteLine();

            return;
        }

        Console.WriteLine(
            "TENANTS REQUIRING REVIEW"
        );

        Console.WriteLine(
            "========================"
        );

        Console.WriteLine();

        foreach (var tenant
                 in tenantsRequiringReview)
        {
            WriteTenant(
                tenant
            );
        }
    }

    private static void WriteTenant(
        TenantBackupReport tenant)
    {
        Console.WriteLine(
            tenant.TenantName
        );

        Console.WriteLine(
            new string(
                '-',
                tenant.TenantName.Length
            )
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Resources checked:     {tenant.RowsChecked}"
        );

        Console.WriteLine(
            $"Completed suppressed:  {tenant.HealthyRowsSuppressed}"
        );

        Console.WriteLine(
            $"Needs review:           {tenant.FindingsCount}"
        );

        Console.WriteLine(
            $"Unknown:                {tenant.UnknownRowsCount}"
        );

        Console.WriteLine();

        foreach (var entry
                 in tenant.Findings)
        {
            Console.WriteLine(
                "NEEDS REVIEW"
            );

            WriteEntry(
                entry
            );
        }

        foreach (var entry
                 in tenant.UnknownRows)
        {
            Console.WriteLine(
                "UNKNOWN"
            );

            WriteEntry(
                entry
            );
        }

        Console.WriteLine();
    }

    private static void WriteEntry(
        BackupReportEntry entry)
    {
        Console.WriteLine(
            $"  Resource: {entry.DeviceName}"
        );

        Console.WriteLine(
            $"  State:    {entry.DeviceState ?? "<missing>"}"
        );

        Console.WriteLine(
            $"  Result:   {entry.LastResult}"
        );

        Console.WriteLine(
            $"  Plan:     {entry.PlanName ?? "<missing>"}"
        );

        Console.WriteLine(
            $"  Time:     {FormatTimestamp(entry.LastBackupRun)}"
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
