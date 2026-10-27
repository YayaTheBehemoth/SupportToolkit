using System.Globalization;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

/// <summary>
/// Writes the full monitoring scope report: summary counts first, then
/// every in-scope and excluded tenant, then discrepancies,
/// acknowledgements and exceptions.
///
/// Only runtime data reaches this output; nothing is persisted.
/// </summary>
public sealed class ConsoleMonitoringScopeReporter
{
    public void Write(
        TextWriter output,
        MonitoringScopeReport report)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        output.WriteLine();
        output.WriteLine("MONITORING SCOPE REPORT");
        output.WriteLine("-----------------------");
        output.WriteLine($"As of: {Date(report.AsOfDate)}");
        output.WriteLine(
            "Entitling items: " +
            (report.EntitlingItems.Count == 0
                ? "<none>"
                : string.Join(", ", report.EntitlingItems)));

        WriteSummary(output, report);
        WriteInScope(output, report);
        WriteExcluded(output, report);
        WriteDiscrepancies(output, report);
        WriteAcknowledged(output, report);
        WriteExceptions(output, report);
    }

    private static void WriteSummary(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, "SUMMARY");

        output.WriteLine(
            $"Acronis customer tenants:  {report.AcronisCustomerTenants}");
        output.WriteLine(
            $"  In scope this run:       {report.InScope.Count}");
        output.WriteLine(
            $"  Excluded:                {report.Excluded.Count}");
        output.WriteLine(
            $"  Needs scope review:      {report.NeedsReview.Count}");
        output.WriteLine(
            $"  Unscoped (no mapping):   {report.UnscopedTenants.Count}");
        output.WriteLine(
            $"Entitled BC customers:     {report.EntitledCustomers}");

        if (!report.ReconciliationRun)
        {
            output.WriteLine(
                "Reconciliation:            not run");
        }
        else
        {
            output.WriteLine(
                $"Discrepancies (open):      {report.Discrepancies.Count}");
            output.WriteLine(
                $"Acknowledged:              {report.Acknowledged.Count}");
            output.WriteLine(
                $"Ambiguous:                 {report.Ambiguous.Count}");
            output.WriteLine(
                $"Unscoped BC records:       {report.UnscopedRecords.Count}");

            if (!report.MonitoredEvidenceSupplied)
            {
                output.WriteLine(
                    "Note: no monitored-tenant evidence was supplied, so " +
                    "\"monitored without entitlement\" cannot be detected.");
            }
        }
    }

    private static void WriteInScope(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, $"IN SCOPE THIS RUN ({report.InScope.Count})");

        if (report.InScope.Count == 0)
        {
            output.WriteLine("  None.");
            return;
        }

        foreach (var tenant in report.InScope
            .OrderBy(tenant => tenant.TenantName, StringComparer.OrdinalIgnoreCase))
        {
            var entitling =
                string.Join(
                    ", ",
                    tenant.EntitlingSubscriptions.Select(
                        subscription =>
                            $"{subscription.SubscriptionNumber} " +
                            $"({subscription.ItemNumber ?? "<item unknown>"})"));

            output.WriteLine(
                $"  {tenant.TenantName} | " +
                $"{tenant.CustomerNumber ?? "<no customer>"} | " +
                entitling);
        }
    }

    private static void WriteExcluded(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, $"EXCLUDED ({report.Excluded.Count})");

        if (report.Excluded.Count == 0)
        {
            output.WriteLine("  None.");
            return;
        }

        foreach (var tenant in report.Excluded
            .OrderBy(tenant => tenant.Reason)
            .ThenBy(tenant => tenant.TenantName, StringComparer.OrdinalIgnoreCase))
        {
            var line =
                $"  {tenant.TenantName} | " +
                (tenant.CustomerNumber is null
                    ? string.Empty
                    : $"{tenant.CustomerNumber} | ") +
                tenant.Detail;

            foreach (var acknowledgement in tenant.Acknowledgements)
            {
                line +=
                    $" | {acknowledgement.Type} acknowledged until " +
                    Date(acknowledgement.ExpiresOn);
            }

            output.WriteLine(line);
        }
    }

    private static void WriteDiscrepancies(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, $"DISCREPANCIES ({report.Discrepancies.Count})");

        if (!report.ReconciliationRun)
        {
            output.WriteLine("  Reconciliation was not run.");
            return;
        }

        if (report.Discrepancies.Count == 0)
        {
            output.WriteLine("  None.");
            return;
        }

        foreach (var item in report.Discrepancies)
        {
            var discrepancy = item.Discrepancy;

            var subscriptions =
                discrepancy.SubscriptionNumbers.Count == 0
                    ? string.Empty
                    : " | " + string.Join(", ", discrepancy.SubscriptionNumbers);

            var resurfaced =
                discrepancy.ExpiredAcknowledgement is null
                    ? string.Empty
                    : " | acknowledgement expired " +
                      Date(discrepancy.ExpiredAcknowledgement.ExpiresOn);

            output.WriteLine(
                $"  {item.TenantName} | " +
                $"{item.CustomerNumber ?? "<no customer>"} | " +
                $"{discrepancy.Key.Type}{subscriptions} | " +
                $"{discrepancy.Reason}{resurfaced}");
        }
    }

    private static void WriteAcknowledged(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, $"ACKNOWLEDGED ({report.Acknowledged.Count})");

        if (report.Acknowledged.Count == 0)
        {
            output.WriteLine("  None.");
            return;
        }

        foreach (var item in report.Acknowledged)
        {
            var acknowledgement =
                item.Discrepancy.Acknowledgement!;

            output.WriteLine(
                $"  {item.TenantName} | {acknowledgement.Type} | " +
                $"acknowledged until {Date(acknowledgement.ExpiresOn)} | " +
                acknowledgement.Reason +
                (acknowledgement.AcknowledgedBy is null
                    ? string.Empty
                    : $" (by {acknowledgement.AcknowledgedBy})"));
        }
    }

    private static void WriteExceptions(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, "EXCEPTIONS");

        var any = false;

        foreach (var notice in report.Notices)
        {
            output.WriteLine($"  Notice: {notice}");
            any = true;
        }

        foreach (var tenant in report.NeedsReview)
        {
            output.WriteLine(
                $"  Needs scope review | {tenant.TenantName} | {tenant.Reason}");
            any = true;
        }

        foreach (var tenant in report.UnscopedTenants)
        {
            output.WriteLine(
                $"  Unscoped tenant | {tenant.TenantName} | {tenant.Reason}");
            any = true;
        }

        foreach (var exception in report.Ambiguous)
        {
            output.WriteLine(
                $"  {ReconciliationBucket.Ambiguous} | {exception.Subject} | " +
                string.Join(" ", exception.Reasons));
            any = true;
        }

        foreach (var exception in report.UnscopedRecords)
        {
            output.WriteLine(
                $"  {ReconciliationBucket.Unscoped} | {exception.Subject} | " +
                string.Join(" ", exception.Reasons));
            any = true;
        }

        if (!any)
        {
            output.WriteLine("  None.");
        }
    }

    private static void Heading(
        TextWriter output,
        string title)
    {
        output.WriteLine();
        output.WriteLine(title);
        output.WriteLine(new string('-', title.Length));
    }

    private static string Date(
        DateOnly date) =>
        date.ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);
}
