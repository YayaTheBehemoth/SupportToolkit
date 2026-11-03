using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>Everything one report is built from. No I/O.</summary>
public sealed record UsageReportInputs(
    IReadOnlyList<SmartSubscriptionHeader> Headers,
    IReadOnlyList<SubscriptionBillingLine> BillingLines,
    IReadOnlyCollection<string> BackupItemNumbers,
    ScopedCustomerParties CustomerNames,
    UsageTenants Tenants,
    AutoMappingResult Matching,
    UsageStorage Storage);

/// <summary>
/// Builds the usage report from what was read. Pure: no I/O, no clock,
/// and no knowledge of which systems the data came from.
///
/// Rules:
///  - Only lines for the backup items count, and only on an Active
///    subscription and not omitted. Other backup lines are counted as
///    excluded and shown, never silently dropped. A line whose
///    subscription cannot be attributed to one customer is counted, not
///    guessed.
///  - A customer is linked to tenants only through accepted matches. A
///    pending match is reported, not used.
///  - A customer's tenants are added up; any unknown tenant figure makes
///    the customer's figure unknown, never zero.
///  - Billed quantity is compared with the total in decimal GB. Nothing
///    is judged.
/// </summary>
public static class UsageReportBuilder
{
    public static Models.UsageReport Build(
        UsageReportInputs inputs,
        DateTimeOffset generatedAt,
        SupportToolkitMode mode)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var items = SubscriptionItemNumbers.ToSet(inputs.BackupItemNumbers);

        var headers =
            inputs.Headers
                .Where(header => !string.IsNullOrWhiteSpace(header.SubscriptionNumber))
                .GroupBy(
                    header => header.SubscriptionNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var perCustomer =
            new Dictionary<string, List<(UsageBillingLine Line, bool Counted)>>(
                StringComparer.OrdinalIgnoreCase);

        var unattributed = 0;

        foreach (var line in inputs.BillingLines)
        {
            var item = SubscriptionItemNumbers.Normalize(line.ItemNumber ?? string.Empty);

            if (!items.Contains(item))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.SubscriptionNumber)
                || !headers.TryGetValue(line.SubscriptionNumber.Trim(), out var owners)
                || owners.Length != 1
                || string.IsNullOrWhiteSpace(owners[0].CustomerNumber))
            {
                unattributed++;
                continue;
            }

            var customer = owners[0].CustomerNumber!.Trim();

            var counted =
                string.Equals(
                    owners[0].Status?.Trim(),
                    SmartSubscriptionStatusVocabulary.English.Active,
                    StringComparison.OrdinalIgnoreCase)
                && line.OmitLine != true;

            if (!perCustomer.TryGetValue(customer, out var list))
            {
                list = [];
                perCustomer[customer] = list;
            }

            list.Add(
                (new UsageBillingLine(
                    line.SubscriptionNumber.Trim(),
                    line.LineNumber,
                    item,
                    line.Description,
                    line.UnitOfMeasure,
                    line.Quantity,
                    line.UnitPrice,
                    line.LineDiscountPercent,
                    line.Amount),
                 counted));
        }

        var customerNames =
            inputs.CustomerNames.Parties
                .Where(party => !string.IsNullOrWhiteSpace(party.Id))
                .GroupBy(party => party.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

        var tenantNames =
            inputs.Tenants.Tenants
                .Where(party => !string.IsNullOrWhiteSpace(party.Id))
                .GroupBy(party => party.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

        var storage =
            new Dictionary<string, TenantStorage>(
                inputs.Storage.ByTenant
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .GroupBy(pair => pair.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First()),
                StringComparer.OrdinalIgnoreCase);

        var tenantsByCustomer =
            inputs.Matching.Accepted
                .GroupBy(link => link.BcCustomerNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(link => link.AcronisTenantId.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var pendingCustomers =
            inputs.Matching.PendingReview
                .Select(link => link.BcCustomerNo.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pendingTenants =
            inputs.Matching.PendingReview
                .Select(link => link.AcronisTenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<UsageReportRow>();

        foreach (var (customer, lines) in perCustomer)
        {
            tenantsByCustomer.TryGetValue(customer, out var tenantIds);
            tenantIds ??= [];

            rows.Add(
                CustomerRow(
                    customer,
                    customerNames.GetValueOrDefault(customer),
                    lines,
                    tenantIds,
                    tenantNames,
                    pendingCustomers.Contains(customer),
                    storage));
        }

        // Customer tenants not linked to any customer that has a backup
        // line: usage that may not be billed. Tenants with no storage, or
        // nothing reported, are counted, not listed.
        var linkedTenants =
            tenantsByCustomer
                .Where(pair => perCustomer.ContainsKey(pair.Key))
                .SelectMany(pair => pair.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tenantsWithoutStorage = 0;
        var tenantsWithoutUsage = 0;

        foreach (var (tenantId, name) in tenantNames
                     .Where(pair => !linkedTenants.Contains(pair.Key)))
        {
            var row =
                TenantRow(
                    tenantId,
                    name,
                    pendingTenants.Contains(tenantId),
                    storage);

            if (row.Total.Bytes == 0m)
            {
                tenantsWithoutStorage++;
                continue;
            }

            if (row.Total.NotReported)
            {
                tenantsWithoutUsage++;
                continue;
            }

            rows.Add(row);
        }

        var ordered =
            rows
                .OrderBy(row => row.Kind)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CustomerNumber, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return new Models.UsageReport(
            generatedAt,
            mode,
            ordered,
            inputs.Matching.PendingReview.Count,
            unattributed,
            inputs.Storage.PeriodStart,
            Notices(inputs, unattributed, tenantsWithoutStorage, tenantsWithoutUsage),
            ReportProblems(inputs));
    }

    private static UsageReportRow CustomerRow(
        string customer,
        string? name,
        List<(UsageBillingLine Line, bool Counted)> lines,
        IReadOnlyList<string> tenantIds,
        IReadOnlyDictionary<string, string> tenantNames,
        bool matchPending,
        IReadOnlyDictionary<string, TenantStorage> storage)
    {
        var problems = new List<string>();
        var remarks = new List<string>();

        var counted = lines.Where(line => line.Counted).Select(line => line.Line).ToArray();
        var excluded = lines.Count - counted.Length;

        decimal? quantity = null;
        decimal? amount = null;

        // Every backup line is on a subscription that is not active, or
        // omitted: not part of the report, listed apart.
        var notActive = counted.Length == 0;

        if (notActive)
        {
            remarks.Add(
                "no active backup line: the subscription is not active, " +
                "or its backup lines are omitted");
        }
        else
        {
            if (counted.Any(line => line.Quantity is null))
            {
                problems.Add("a line quantity is missing");
            }
            else
            {
                quantity = counted.Sum(line => line.Quantity!.Value);
            }

            if (counted.Any(line => line.Amount is null))
            {
                problems.Add("a line amount is missing");
            }
            else
            {
                amount = counted.Sum(line => line.Amount!.Value);
            }
        }

        if (excluded > 0 && !notActive)
        {
            remarks.Add(
                $"{excluded} backup line(s) not counted (subscription not " +
                "active, or the line is omitted)");
        }

        UsageMeasurement total;
        UsageMeasurement backup;
        UsageMeasurement microsoft365;

        if (tenantIds.Count == 0)
        {
            var reason =
                matchPending
                    ? "the match to a tenant needs review"
                    : "no tenant matched";

            if (!notActive)
            {
                problems.Add(reason);
            }

            total = UsageMeasurement.Unknown(reason);
            backup = UsageMeasurement.Unknown(reason);
            microsoft365 = UsageMeasurement.Unknown(reason);
        }
        else
        {
            (total, backup, microsoft365) = Measure(tenantIds, storage);

            if (total.NotReported)
            {
                // Set up, but nothing has been reported yet: not a data
                // problem, something to raise with whoever set it up.
                remarks.Add("no usage has been reported yet (" + total.Problem + ")");
            }
            else
            {
                if (!total.IsKnown && !notActive)
                {
                    problems.Add("total usage unknown: " + total.Problem);
                }

                AddComponentRemarks(remarks, total, backup, microsoft365);
            }
        }

        var kind =
            notActive
                ? UsageRowKind.NotActive
                : tenantIds.Count > 0 && total.NotReported
                    ? UsageRowKind.AwaitingUsage
                    : UsageRowKind.Customer;

        decimal? difference =
            kind == UsageRowKind.Customer
            && quantity is not null
            && total.Gb is not null
                ? quantity - total.Gb
                : null;

        return new UsageReportRow(
            kind,
            customer,
            name,
            tenantIds,
            tenantIds
                .Select(id => tenantNames.GetValueOrDefault(id) ?? string.Empty)
                .ToArray(),
            lines.Select(line => line.Line).ToArray(),
            excluded,
            quantity,
            amount,
            total,
            backup,
            microsoft365,
            difference,
            problems,
            remarks);
    }

    private static UsageReportRow TenantRow(
        string tenantId,
        string name,
        bool matchPending,
        IReadOnlyDictionary<string, TenantStorage> storage)
    {
        var problems = new List<string>();
        var remarks = new List<string>();

        var (total, backup, microsoft365) = Measure([tenantId], storage);

        if (!total.IsKnown)
        {
            problems.Add("total usage unknown: " + total.Problem);
        }

        if (matchPending)
        {
            remarks.Add("a match to a customer needs review");
        }

        AddComponentRemarks(remarks, total, backup, microsoft365);

        return new UsageReportRow(
            UsageRowKind.TenantWithoutSubscription,
            null,
            null,
            [tenantId],
            [name],
            [],
            0,
            null,
            null,
            total,
            backup,
            microsoft365,
            null,
            problems,
            remarks);
    }

    /// <summary>
    /// The three figures for the given tenants, added up. A tenant with no
    /// storage entry had nothing reported.
    /// </summary>
    private static (UsageMeasurement Total, UsageMeasurement Backup, UsageMeasurement Microsoft365)
        Measure(
            IReadOnlyList<string> tenantIds,
            IReadOnlyDictionary<string, TenantStorage> storage)
    {
        var parts =
            tenantIds
                .Select(
                    id => storage.TryGetValue(id.Trim(), out var figures)
                        ? figures
                        : TenantStorage.NothingReported("no usage returned"))
                .ToArray();

        return (
            UsageMeasurement.Sum(parts.Select(part => part.Total).ToArray()),
            UsageMeasurement.Sum(parts.Select(part => part.Backup).ToArray()),
            UsageMeasurement.Sum(parts.Select(part => part.Microsoft365).ToArray()));
    }

    private static void AddComponentRemarks(
        List<string> remarks,
        UsageMeasurement total,
        UsageMeasurement backup,
        UsageMeasurement microsoft365)
    {
        if (!backup.IsKnown)
        {
            remarks.Add("backup storage unknown: " + backup.Problem);
        }

        if (!microsoft365.IsKnown)
        {
            remarks.Add("Microsoft 365 storage unknown: " + microsoft365.Problem);
        }

        // In the portal, total is backup plus Microsoft 365. When it is
        // not, the figures are worth checking before they are relied on.
        if (total.IsKnown
            && backup.IsKnown
            && microsoft365.IsKnown
            && total.Bytes != backup.Bytes + microsoft365.Bytes)
        {
            remarks.Add(
                "total is not backup + Microsoft 365 " +
                $"({Gb(total.Gb)} GB against {Gb(backup.Gb + microsoft365.Gb)} GB)");
        }
    }

    private static IReadOnlyList<string> Notices(
        UsageReportInputs inputs,
        int unattributed,
        int tenantsWithoutStorage,
        int tenantsWithoutUsage)
    {
        var notices = new List<string>
        {
            "Usage is compared in decimal GB (1 GB = 1,000,000,000 bytes), " +
            "the unit the billed quantity is in."
        };

        if (inputs.Tenants.Notice is not null)
        {
            notices.Add(inputs.Tenants.Notice);
        }

        if (unattributed > 0)
        {
            notices.Add(
                $"{unattributed} backup line(s) belong to a subscription that " +
                "is missing or not tied to one customer, so they are not " +
                "counted for anyone.");
        }

        if (tenantsWithoutStorage > 0)
        {
            notices.Add(
                $"{tenantsWithoutStorage} other customer tenant(s) have no " +
                "storage and are not listed.");
        }

        if (tenantsWithoutUsage > 0)
        {
            notices.Add(
                $"{tenantsWithoutUsage} other customer tenant(s) have no " +
                "usage reported yet and are not listed.");
        }

        notices.AddRange(inputs.Storage.Notices);

        return notices;
    }

    private static IReadOnlyList<string> ReportProblems(
        UsageReportInputs inputs)
    {
        var problems = new List<string>();

        if (inputs.CustomerNames.Failure is not null)
        {
            problems.Add(
                "Customer names could not be read from the billing system, so " +
                "customers could not be matched to tenants by name.");
        }

        if (inputs.Matching.Exceptions.Count > 0)
        {
            problems.Add(
                $"{inputs.Matching.Exceptions.Count} customer match(es) are " +
                "conflicting and were not used.");
        }

        return problems;
    }

    internal static string Gb(decimal? value) =>
        value is null
            ? "unknown"
            : value.Value.ToString("0.00", CultureInfo.InvariantCulture);
}
