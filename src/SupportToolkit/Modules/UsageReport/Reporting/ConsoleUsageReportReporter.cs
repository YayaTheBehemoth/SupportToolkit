using System.Globalization;
using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Modules.UsageReport.Reporting;

/// <summary>
/// Prints a usage report. Counts only by default; customer names,
/// amounts and per-customer rows only when asked for, because they are
/// customer and financial data.
/// </summary>
public static class ConsoleUsageReportReporter
{
    private const int NameWidth = 32;

    public static void Write(
        TextWriter output,
        Models.UsageReport report,
        bool showNames)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(report);

        output.WriteLine();
        output.WriteLine("USAGE REPORT (Acronis storage against Business Central billing)");
        output.WriteLine("---------------------------------------------------------------");
        output.WriteLine($"Mode:                {report.Mode.ToString().ToLowerInvariant()}");
        output.WriteLine(
            "Generated (UTC):     " +
            report.GeneratedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        output.WriteLine($"Acronis usage from:  {report.UsagePeriodStart ?? "unknown"}");
        output.WriteLine();
        output.WriteLine($"Customers with a backup subscription:   {report.Customers.Count()}");
        output.WriteLine($"  compared:                             {report.CustomersCompared}");
        output.WriteLine($"  of which billed differs from Acronis: {report.CustomersWithDifference}");
        output.WriteLine($"  unknown (see why below):              {report.CustomersUnknown}");
        output.WriteLine($"Set up, no usage reported yet:          {report.AwaitingUsage.Count()}");
        output.WriteLine($"Not active (left out of the report):    {report.NotActive.Count()}");
        output.WriteLine($"Tenants with storage, no subscription:  {report.TenantsWithoutSubscription.Count()}");
        output.WriteLine($"Customer matches waiting for review:    {report.PendingMatches}");

        foreach (var problem in report.Problems)
        {
            output.WriteLine();
            output.WriteLine("PROBLEM: " + problem);
        }

        output.WriteLine();

        foreach (var notice in report.Notices)
        {
            output.WriteLine("Note: " + notice);
        }

        output.WriteLine();
        output.WriteLine(
            report.IsComplete
                ? "Complete: every customer in the report was compared."
                : "INCOMPLETE: not every customer could be compared. Nothing unknown is shown as zero.");

        var awaiting = report.AwaitingUsage.Count();

        if (awaiting > 0)
        {
            output.WriteLine(
                $"{awaiting} customer(s) are set up but have no Acronis usage " +
                "yet; they are listed apart, not compared.");
        }

        if (!showNames)
        {
            output.WriteLine();
            output.WriteLine(
                "Customer names, amounts and rows hidden. Use --names to show them.");
            return;
        }

        output.WriteLine();
        output.WriteLine("CUSTOMERS (GB = 1,000,000,000 bytes)");
        output.WriteLine();
        output.WriteLine(
            $"{"Customer",-NameWidth}  {"Billed GB",10}  {"Amount",10}  " +
            $"{"Acronis GB",10}  {"Backup",9}  {"M365",9}  {"Diff GB",9}");

        foreach (var row in report.Customers)
        {
            output.WriteLine(
                $"{Fit(row.Name),-NameWidth}  {Number(row.BilledQuantity),10}  " +
                $"{Number(row.BilledAmount),10}  {Number(row.Total.Gb),10}  " +
                $"{Number(row.Backup.Gb),9}  {Number(row.Microsoft365.Gb),9}  " +
                $"{Signed(row.DifferenceGb),9}");
        }

        var tenants = report.TenantsWithoutSubscription.ToArray();

        if (tenants.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("TENANTS WITH STORAGE BUT NO BACKUP SUBSCRIPTION");
            output.WriteLine();

            foreach (var row in tenants)
            {
                output.WriteLine(
                    $"{Fit(row.Name),-NameWidth}  {Number(row.Total.Gb),10} GB");
            }
        }

        var awaitingRows = report.AwaitingUsage.ToArray();

        if (awaitingRows.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("SET UP BUT NO USAGE REPORTED YET (raise with whoever set them up)");
            output.WriteLine();

            foreach (var row in awaitingRows)
            {
                output.WriteLine(
                    $"{Fit(row.Name),-NameWidth}  billed {Number(row.BilledQuantity)} GB");
            }
        }

        var notActive = report.NotActive.ToArray();

        if (notActive.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("NOT ACTIVE (left out of the report)");
            output.WriteLine();

            foreach (var row in notActive)
            {
                output.WriteLine(
                    $"{Fit(row.Name),-NameWidth}  Acronis {Number(row.Total.Gb)} GB");
            }
        }

        // Only rows in the report itself; the lists above explain theirs.
        var attention =
            report.Rows
                .Where(row => row.Kind
                    is UsageRowKind.Customer
                    or UsageRowKind.TenantWithoutSubscription)
                .Where(row => row.Problems.Count > 0 || row.Remarks.Count > 0)
                .ToArray();

        if (attention.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("WHY SOME ROWS NEED A LOOK");
            output.WriteLine();

            foreach (var row in attention)
            {
                output.WriteLine(row.Name);

                foreach (var problem in row.Problems)
                {
                    output.WriteLine("  unknown: " + problem);
                }

                foreach (var remark in row.Remarks)
                {
                    output.WriteLine("  check:   " + remark);
                }
            }
        }
    }

    private static string Number(decimal? value) =>
        value is null
            ? "unknown"
            : value.Value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Signed(decimal? value) =>
        value is null
            ? "unknown"
            : value.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    private static string Fit(string value) =>
        value.Length <= NameWidth ? value : value[..(NameWidth - 3)] + "...";
}
