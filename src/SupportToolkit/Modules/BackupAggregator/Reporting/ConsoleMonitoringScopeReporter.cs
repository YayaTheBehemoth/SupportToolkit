using System.Globalization;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

/// <summary>
/// Writes the full monitoring scope report: summary counts first, then
/// every in-scope and excluded tenant, then discrepancies and
/// exceptions.
///
/// Only runtime data reaches this output; nothing is persisted.
/// </summary>
public sealed class ConsoleMonitoringScopeReporter
{
    /// <summary>
    /// Writes the report. The default view is the header, the summary,
    /// any notices and the tenants in scope this run. With detailed
    /// set, the automatic mappings, excluded tenants, discrepancies and
    /// exceptions follow; the summary counts say whether any exist.
    /// </summary>
    public void Write(
        TextWriter output,
        MonitoringScopeReport report,
        bool detailed = true)
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

        if (!detailed)
        {
            // Warnings about the run itself are never hidden.
            foreach (var notice in report.Notices)
            {
                output.WriteLine($"Notice: {notice}");
            }

            WriteInScope(output, report);
            WriteStrayTenants(output, report);

            if (HasHiddenDetail(report))
            {
                output.WriteLine();
                output.WriteLine(
                    "More detail (automatic mappings, excluded tenants, " +
                    "discrepancies, exceptions) is shown when " +
                    "SUPPORTTOOLKIT_DEBUG=true.");
            }

            return;
        }

        WriteAutomaticMappings(output, report);
        WriteInScope(output, report);
        WriteStrayTenants(output, report);
        WriteExcluded(output, report);
        WriteDiscrepancies(output, report);
        WriteExceptions(output, report);
    }

    private static bool HasHiddenDetail(
        MonitoringScopeReport report)
    {
        var mappings = report.AutomaticMappings;

        return report.Excluded.Count > 0
            || report.NeedsReview.Count > 0
            || report.UnscopedTenants.Count > 0
            || report.Discrepancies.Count > 0
            || report.Ambiguous.Count > 0
            || report.UnscopedRecords.Count > 0
            || mappings is not null;
    }

    private static void WriteSummary(
        TextWriter output,
        MonitoringScopeReport report)
    {
        Heading(output, "SUMMARY");

        output.WriteLine(
            $"Acronis tenants:           {report.AcronisCustomerTenants}");
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

        if (report.StrayTenants is { } stray)
        {
            output.WriteLine(
                "Leak candidates:           " +
                (stray.LeakCheckAvailable
                    ? stray.LeakCandidates.Count.ToString(
                        CultureInfo.InvariantCulture)
                    : "unavailable"));
            output.WriteLine(
                $"Unmatched tenants:         {stray.Unmatched.Count}");
        }

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

    private static void WriteAutomaticMappings(
        TextWriter output,
        MonitoringScopeReport report)
    {
        var mappings = report.AutomaticMappings;

        if (mappings is null)
        {
            return;
        }

        Heading(output, "AUTOMATIC MAPPING (recomputed every run)");

        output.WriteLine(
            $"Accepted automatically:    {mappings.Accepted}");

        foreach (var method in mappings.AcceptedByMethod
            .OrderBy(pair => pair.Key))
        {
            output.WriteLine(
                $"  by {method.Key}: {method.Value}");
        }

        output.WriteLine(
            $"Pending review:            {mappings.PendingReview}");
        output.WriteLine(
            $"Ambiguous or conflicting:  {mappings.Exceptions}");

        if (mappings.NameFailure is not null)
        {
            output.WriteLine(
                "BC customer names could not be read, so no name match " +
                $"was possible: {mappings.NameFailure}");
        }
        else if (mappings.CustomersWithoutName > 0
            || mappings.CustomersWithConflictingNames > 0)
        {
            output.WriteLine(
                $"BC customers without a usable name: " +
                $"{mappings.CustomersWithoutName} " +
                $"(conflicting names: {mappings.CustomersWithConflictingNames})");
        }

        output.WriteLine(
            "BC customers with no Acronis tenant (suspended or blocked " +
            "ones are ignored): " +
            mappings.CustomersWithoutTenant.Count);

        foreach (var gap in mappings.CustomersWithoutTenant
            .OrderBy(gap => gap.CustomerNumber, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine(
                $"  {gap.CustomerNumber} | " +
                $"{gap.CustomerName ?? "<no name>"} | {gap.State}");
        }

        if (mappings.EntitledWithoutServiceItem.Count > 0)
        {
            output.WriteLine(
                "Entitled customers without any service item " +
                "(the add-on implies one): " +
                mappings.EntitledWithoutServiceItem.Count);

            foreach (var customer in mappings.EntitledWithoutServiceItem)
            {
                output.WriteLine(
                    $"  {customer.CustomerNumber} | " +
                    $"{customer.CustomerName ?? "<no name>"}");
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
            if (tenant.IsOperatorTenant)
            {
                output.WriteLine(
                    $"  {tenant.TenantName} | " +
                    "operator's own tenant (not billed, always included)");

                continue;
            }

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

    private static void WriteStrayTenants(
        TextWriter output,
        MonitoringScopeReport report)
    {
        var check = report.StrayTenants;

        if (check is null)
        {
            return;
        }

        Heading(output, $"STRAY TENANT CHECK ({check.Total})");

        if (!check.LeakCheckAvailable)
        {
            output.WriteLine(
                "  Leak check unavailable: no service items are " +
                "configured, so a customer without a backup item " +
                "cannot be told apart from one with it.");
        }
        else if (check.LeakCandidates.Count == 0)
        {
            output.WriteLine("  Leak candidates: none.");
        }
        else
        {
            output.WriteLine(
                $"  Leak candidates ({check.LeakCandidates.Count}): " +
                "matched customer holds neither the backup nor the " +
                "monitoring item.");

            foreach (var tenant in check.LeakCandidates)
            {
                output.WriteLine(
                    $"    {tenant.TenantName} | " +
                    $"{tenant.CustomerNumber} | {tenant.Reason}");
            }
        }

        if (check.Unmatched.Count == 0)
        {
            output.WriteLine("  Unmatched tenants: none.");
            return;
        }

        output.WriteLine(
            $"  Unmatched tenants ({check.Unmatched.Count}): needs " +
            "review.");

        foreach (var tenant in check.Unmatched)
        {
            output.WriteLine(
                $"    {tenant.TenantName} | {tenant.Reason}");
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
            output.WriteLine(
                $"  {tenant.TenantName} | " +
                (tenant.CustomerNumber is null
                    ? string.Empty
                    : $"{tenant.CustomerNumber} | ") +
                tenant.Detail);
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

            output.WriteLine(
                $"  {item.TenantName} | " +
                $"{item.CustomerNumber ?? "<no customer>"} | " +
                $"{discrepancy.Key.Type}{subscriptions} | " +
                discrepancy.Reason);
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
