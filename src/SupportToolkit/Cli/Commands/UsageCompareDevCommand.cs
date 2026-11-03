using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Usage;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;

namespace SupportToolkit.Cli.Commands;

public sealed record UsageCompareOptions(
    bool ShowNames,
    string Metric);

/// <summary>An accepted link between an Acronis tenant and a BC customer.</summary>
public sealed record TenantLink(
    string TenantId,
    string CustomerNumber);

/// <summary>
/// One line of the comparison: a Business Central customer's billed
/// quantity next to what Acronis reports for the customer's tenant(s).
///
/// Nothing is judged. A side that is missing or ambiguous is null and
/// the status says why; it is never treated as zero. Differences are
/// BC minus Acronis, so a negative number means BC is lower.
/// </summary>
public sealed record UsageComparisonRow(
    string? CustomerNumber,
    string TenantIds,
    string TenantNames,
    decimal? BcQuantity,
    string BcItems,
    int BcLinesExcluded,
    decimal? AcronisBytes,
    decimal? AcronisGib,
    decimal? AcronisGb,
    decimal? DiffGib,
    decimal? DiffGb,
    decimal? DiffPercentGib,
    string Status)
{
    public bool IsCompared => Status == "ok";
}

/// <summary>
/// Pure comparison of Business Central subscription quantities with
/// Acronis usage. No I/O.
/// </summary>
public static class UsageComparison
{
    public const decimal BytesPerGib = 1_073_741_824m;
    public const decimal BytesPerGb = 1_000_000_000m;

    public static IReadOnlyList<UsageComparisonRow> Build(
        IReadOnlyList<TenantDto> tenants,
        AcronisUsageResult usage,
        IReadOnlyList<TenantLink> links,
        SmartSubscriptionEvidence evidence,
        IReadOnlyCollection<string> itemNumbers,
        string metric)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(itemNumbers);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);

        var items = SubscriptionItemNumbers.ToSet(itemNumbers);

        var tenantsById =
            tenants
                .Where(tenant => !string.IsNullOrWhiteSpace(tenant.Id))
                .GroupBy(
                    tenant => tenant.Id.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        var linkedTenantsByCustomer =
            links
                .GroupBy(
                    link => link.CustomerNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(link => link.TenantId.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var bcByCustomer = ReadBcSide(evidence, items);

        var customers =
            bcByCustomer.Keys
                .Concat(linkedTenantsByCustomer.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var rows = new List<UsageComparisonRow>();

        foreach (var customer in customers)
        {
            bcByCustomer.TryGetValue(customer, out var bc);

            linkedTenantsByCustomer.TryGetValue(
                customer,
                out var tenantIds);

            rows.Add(
                BuildRow(
                    customer,
                    tenantIds ?? [],
                    tenantsById,
                    usage,
                    bc,
                    metric));
        }

        // Customer tenants with no accepted BC link still show what
        // Acronis reports, so usage without a billing counterpart
        // cannot go unnoticed.
        var linkedTenants =
            links
                .Select(link => link.TenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in tenants
                     .Where(
                         tenant => string.Equals(
                             tenant.Kind,
                             "customer",
                             StringComparison.OrdinalIgnoreCase)
                             && !linkedTenants.Contains(tenant.Id.Trim()))
                     .OrderBy(
                         tenant => tenant.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         tenant => tenant.Id,
                         StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(
                BuildRow(
                    null,
                    [tenant.Id.Trim()],
                    tenantsById,
                    usage,
                    null,
                    metric));
        }

        return rows;
    }

    private sealed record BcSide(
        decimal? Quantity,
        string Items,
        int Excluded,
        string? Problem);

    /// <summary>
    /// Per customer: the quantity on the configured items. Only lines on
    /// an Active subscription that are not omitted count. Anything else
    /// is counted as excluded and shown, never silently dropped.
    /// </summary>
    private static Dictionary<string, BcSide> ReadBcSide(
        SmartSubscriptionEvidence evidence,
        HashSet<string> items)
    {
        var headers =
            evidence.Headers
                .Where(
                    header => !string.IsNullOrWhiteSpace(
                        header.SubscriptionNumber))
                .GroupBy(
                    header => header.SubscriptionNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var perCustomer =
            new Dictionary<string, List<(string Item, decimal? Quantity, bool Used)>>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var line in evidence.Lines)
        {
            var item = SubscriptionItemNumbers.Normalize(line.ItemNumber);

            if (!items.Contains(item))
            {
                continue;
            }

            if (!headers.TryGetValue(
                    line.SubscriptionNumber.Trim(),
                    out var owners)
                || owners.Length != 1
                || string.IsNullOrWhiteSpace(owners[0].CustomerNumber))
            {
                // Cannot say whose line this is: not attributed to
                // anyone, and not counted for anyone.
                continue;
            }

            var customer = owners[0].CustomerNumber!.Trim();

            var used =
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

            list.Add((item, line.Quantity, used));
        }

        var result =
            new Dictionary<string, BcSide>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var (customer, lines) in perCustomer)
        {
            var used = lines.Where(line => line.Used).ToArray();

            var excluded = lines.Count - used.Length;

            var itemText =
                string.Join(
                    ";",
                    used
                        .GroupBy(
                            line => line.Item,
                            StringComparer.OrdinalIgnoreCase)
                        .OrderBy(
                            group => group.Key,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(
                            group => group.Key + ":" +
                                (group.Any(line => line.Quantity is null)
                                    ? "unknown"
                                    : UsageNumber(
                                        group.Sum(
                                            line => line.Quantity!.Value)))));

            if (used.Length == 0)
            {
                result[customer] =
                    new BcSide(
                        null,
                        string.Empty,
                        excluded,
                        "no active line on the configured items");
            }
            else if (used.Any(line => line.Quantity is null))
            {
                result[customer] =
                    new BcSide(
                        null,
                        itemText,
                        excluded,
                        "a line quantity is missing");
            }
            else
            {
                result[customer] =
                    new BcSide(
                        used.Sum(line => line.Quantity!.Value),
                        itemText,
                        excluded,
                        null);
            }
        }

        return result;
    }

    private static UsageComparisonRow BuildRow(
        string? customer,
        IReadOnlyList<string> tenantIds,
        IReadOnlyDictionary<string, TenantDto> tenantsById,
        AcronisUsageResult usage,
        BcSide? bc,
        string metric)
    {
        var problems = new List<string>();

        if (customer is not null && bc is null)
        {
            problems.Add("no line on the configured items");
        }
        else if (bc?.Problem is not null)
        {
            problems.Add(bc.Problem);
        }

        if (customer is null)
        {
            problems.Add("tenant not linked to a BC customer");
        }

        if (tenantIds.Count == 0)
        {
            problems.Add("no Acronis tenant linked");
        }

        decimal? bytes = null;
        var acronisOk = tenantIds.Count > 0;
        decimal sum = 0m;

        foreach (var tenantId in tenantIds)
        {
            var measured =
                MeasureTenant(tenantId, usage, metric, out var problem);

            if (measured is null)
            {
                acronisOk = false;
                problems.Add(problem!);
            }
            else
            {
                sum += measured.Value;
            }
        }

        if (acronisOk)
        {
            bytes = sum;
        }

        decimal? gib = bytes / BytesPerGib;
        decimal? gb = bytes / BytesPerGb;

        decimal? diffGib =
            bc?.Quantity is not null && gib is not null
                ? bc.Quantity - gib
                : null;

        decimal? diffGb =
            bc?.Quantity is not null && gb is not null
                ? bc.Quantity - gb
                : null;

        decimal? percent =
            diffGib is not null && gib is > 0m
                ? diffGib / gib * 100m
                : null;

        return new UsageComparisonRow(
            customer,
            string.Join(";", tenantIds),
            string.Join(
                ";",
                tenantIds.Select(
                    id => tenantsById.TryGetValue(id, out var tenant)
                        ? tenant.Name
                        : string.Empty)),
            bc?.Quantity,
            bc?.Items ?? string.Empty,
            bc?.Excluded ?? 0,
            bytes,
            gib,
            gb,
            diffGib,
            diffGb,
            percent,
            problems.Count == 0
                ? "ok"
                : "unknown: " + string.Join("; ", problems));
    }

    /// <summary>
    /// The bytes of the named metric for one tenant, or null with a
    /// reason. Several measurements of the metric (for example in more
    /// than one edition) are ambiguous and are not summed.
    /// </summary>
    private static decimal? MeasureTenant(
        string tenantId,
        AcronisUsageResult usage,
        string metric,
        out string? problem)
    {
        problem = null;

        if (!Guid.TryParse(tenantId, out var parsed)
            || !usage.ByTenant.TryGetValue(
                parsed.ToString("D"),
                out var measurements)
            || measurements.Count == 0)
        {
            problem = "no Acronis usage returned";
            return null;
        }

        var matching =
            measurements
                .Where(
                    item => string.Equals(
                        item.MetricName,
                        metric,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (matching.Length == 0)
        {
            problem = $"metric {metric} not reported";
            return null;
        }

        if (matching.Length > 1)
        {
            problem =
                $"{matching.Length} {metric} measurements (editions: " +
                string.Join(
                    ", ",
                    matching.Select(item => item.Edition ?? "<none>")) +
                "), not summed";
            return null;
        }

        var only = matching[0];

        if (only.Value is null)
        {
            problem = $"{metric} value missing";
            return null;
        }

        if (!string.Equals(
                only.Unit,
                "bytes",
                StringComparison.OrdinalIgnoreCase))
        {
            problem =
                $"{metric} unit is {only.Unit ?? "missing"}, not bytes";
            return null;
        }

        return only.Value;
    }

    internal static string UsageNumber(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Developer-only, read-only diagnostic: for each Business Central
/// customer, what its backup subscription lines say next to what
/// Acronis reports for its tenant(s).
///
/// It reads and prints. It sets no threshold, passes no verdict and
/// feeds no report. The items compared are the service items already
/// configured for the backup aggregator; configuration is not changed.
///
/// Aggregates only by default. --names prints one row per customer
/// (customer names and numbers) to the console. Nothing is written to
/// a file.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class UsageCompareDevCommand
{
    public const string DefaultMetric = "storage_total";

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public UsageCompareDevCommand(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));
    }

    public static bool TryParseArguments(
        IReadOnlyList<string> args,
        out UsageCompareOptions? options,
        out string error)
    {
        options = null;
        error =
            "Usage: SupportToolkit dev usage-compare --production " +
            "[--metric <name>] [--names]";

        if (args.Count == 0
            || !string.Equals(
                args[0],
                "--production",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var showNames = false;
        string? metric = null;

        for (var index = 1; index < args.Count; index++)
        {
            if (string.Equals(
                    args[index],
                    "--names",
                    StringComparison.OrdinalIgnoreCase)
                && !showNames)
            {
                showNames = true;
            }
            else if (string.Equals(
                         args[index],
                         "--metric",
                         StringComparison.OrdinalIgnoreCase)
                     && metric is null
                     && index + 1 < args.Count
                     && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                metric = args[++index].Trim();
            }
            else
            {
                return false;
            }
        }

        options =
            new UsageCompareOptions(
                showNames,
                metric ?? DefaultMetric);

        error = string.Empty;

        return true;
    }

    public async Task<int> RunAsync(
        TextWriter output,
        UsageCompareOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        var serviceItems =
            await _configurationResolver.GetServiceItemNumbersAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        if (serviceItems.Count == 0)
        {
            await Console.Error.WriteLineAsync(
                "No service items are configured for the backup " +
                "aggregator, so there is nothing to compare.");
            return 1;
        }

        var monitoringItems =
            await _configurationResolver.GetMonitoringItemNumbersAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        var bundle =
            await BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);

        output.WriteLine("Reading Business Central (read-only)...");

        var scope =
            await new MonitoringScopeService(
                new BusinessCentralSmartSubscriptionEvidenceSource(
                    bundle.Provider,
                    BusinessCentralSourceContext.From(bundle.Options)))
                .ResolveAsync(
                    monitoringItems,
                    DateOnly.FromDateTime(DateTime.Today),
                    cancellationToken,
                    serviceItems);

        var bcParties =
            await new BusinessCentralCustomerNameSource(bundle.Provider)
                .GetCustomerPartiesAsync(
                    scope.ServiceCustomerNumbers.ToArray(),
                    cancellationToken);

        output.WriteLine("Reading Acronis tenants and usage (read-only)...");

        using var session =
            await AcronisDevSession.CreateAsync(
                _configurationResolver,
                cancellationToken);

        var tenants =
            await session.Provider.GetTenantsAsync(cancellationToken);

        var acronis = AcronisCustomerParties.ForResolution(tenants);

        var mapping =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(
                    acronis.Parties,
                    bcParties.Parties,
                    cancellationToken: cancellationToken);

        var requestIds =
            tenants
                .Where(
                    tenant => string.Equals(
                        tenant.Kind,
                        "customer",
                        StringComparison.OrdinalIgnoreCase)
                        && Guid.TryParse(tenant.Id, out _))
                .Select(tenant => tenant.Id)
                .ToArray();

        var usage =
            await session.UsageProvider.GetUsagesAsync(
                requestIds,
                cancellationToken);

        var rows =
            UsageComparison.Build(
                tenants,
                usage,
                mapping.Accepted
                    .Select(
                        record => new TenantLink(
                            record.AcronisTenantId,
                            record.BcCustomerNo))
                    .ToArray(),
                scope.Evidence,
                serviceItems,
                options.Metric);

        return WriteReport(
            output,
            rows,
            serviceItems,
            options.Metric,
            mapping.PendingReview.Count,
            options.ShowNames);
    }

    public static int WriteReport(
        TextWriter output,
        IReadOnlyList<UsageComparisonRow> rows,
        IReadOnlyCollection<string> itemNumbers,
        string metric,
        int pendingReviewLinks,
        bool showNames)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(rows);

        var incomplete =
            WriteSummary(
                output,
                rows,
                itemNumbers,
                metric,
                pendingReviewLinks);

        if (!showNames)
        {
            output.WriteLine();
            output.WriteLine(
                "Names and customer numbers hidden. Use --names for " +
                "one row per customer.");

            return incomplete ? 2 : 0;
        }

        output.WriteLine();

        output.WriteLine(
            "PER-CUSTOMER ROWS (contain customer names)");
        output.WriteLine(
            "------------------------------------------");
        WriteTable(output, rows);

        return incomplete ? 2 : 0;
    }

    private static bool WriteSummary(
        TextWriter output,
        IReadOnlyList<UsageComparisonRow> rows,
        IReadOnlyCollection<string> itemNumbers,
        string metric,
        int pendingReviewLinks)
    {
        var compared = rows.Where(row => row.IsCompared).ToArray();
        var unknown = rows.Where(row => !row.IsCompared).ToArray();

        output.WriteLine();
        output.WriteLine("USAGE COMPARISON (read-only, nothing judged)");
        output.WriteLine("--------------------------------------------");
        output.WriteLine(
            $"Acronis metric:       {metric} (bytes, as reported)");
        output.WriteLine(
            $"Business Central items: {itemNumbers.Count} configured " +
            "service item(s)");
        output.WriteLine($"Rows:                 {rows.Count}");
        output.WriteLine($"  Compared:           {compared.Length}");
        output.WriteLine($"  Unknown (not compared): {unknown.Length}");

        foreach (var group in unknown
                     .SelectMany(row => Reasons(row.Status))
                     .GroupBy(reason => reason)
                     .OrderByDescending(group => group.Count())
                     .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"    {group.Count(),3}  {group.Key}");
        }

        output.WriteLine(
            $"Link suggestions awaiting review (not used): " +
            pendingReviewLinks);

        if (compared.Length > 0)
        {
            output.WriteLine();
            output.WriteLine(
                "Difference = BC quantity minus Acronis; negative " +
                "means BC is lower.");

            WriteDistribution(
                output,
                "vs GiB (1 GB = 2^30 bytes)",
                compared.Select(row => row.DiffGib!.Value).ToArray());

            WriteDistribution(
                output,
                "vs GB  (1 GB = 10^9 bytes)",
                compared.Select(row => row.DiffGb!.Value).ToArray());

            var percents =
                compared
                    .Where(row => row.DiffPercentGib is not null)
                    .Select(row => row.DiffPercentGib!.Value)
                    .ToArray();

            if (percents.Length > 0)
            {
                WriteDistribution(
                    output,
                    "percent of Acronis GiB",
                    percents);
            }

            var diffs = compared.Select(row => row.DiffGib!.Value).ToArray();

            output.WriteLine(
                $"  BC below Acronis (GiB): {diffs.Count(d => d < 0m)}, " +
                $"equal: {diffs.Count(d => d == 0m)}, " +
                $"BC above: {diffs.Count(d => d > 0m)}");
        }

        output.WriteLine();
        output.WriteLine(
            "Only lines on Active subscriptions that are not omitted " +
            "count as BC quantity; other lines are shown as excluded. " +
            "A blocked subscription is not treated differently. " +
            "Acronis figures refresh every few hours and are not " +
            "documented as a billing source.");

        return unknown.Length > 0;
    }

    private static IEnumerable<string> Reasons(string status) =>
        status
            .Replace("unknown: ", string.Empty)
            .Split("; ", StringSplitOptions.RemoveEmptyEntries);

    private static void WriteDistribution(
        TextWriter output,
        string label,
        decimal[] values)
    {
        var sorted = values.Order().ToArray();

        output.WriteLine(
            $"  {label}: min {AcronisUsageDevCommand.Format(sorted[0])}, " +
            $"median {AcronisUsageDevCommand.Format(AcronisUsageDevCommand.Median(sorted))}, " +
            $"p90 {AcronisUsageDevCommand.Format(AcronisUsageDevCommand.Percentile(sorted, 0.9m))}, " +
            $"max {AcronisUsageDevCommand.Format(sorted[^1])}");
    }

    private static void WriteTable(
        TextWriter output,
        IReadOnlyList<UsageComparisonRow> rows)
    {
        output.WriteLine(
            $"{"customer",-10} {"tenant",-28} {"BC qty",10} " +
            $"{"Acronis GiB",12} {"Acronis GB",12} {"diff GiB",10} " +
            $"{"diff GB",10} {"diff %",8}  items / status");

        foreach (var row in rows)
        {
            var name =
                row.TenantNames.Length > 28
                    ? row.TenantNames[..27] + "~"
                    : row.TenantNames;

            output.WriteLine(
                $"{row.CustomerNumber ?? "-",-10} {name,-28} " +
                $"{Show(row.BcQuantity),10} {Show(row.AcronisGib),12} " +
                $"{Show(row.AcronisGb),12} {Show(row.DiffGib),10} " +
                $"{Show(row.DiffGb),10} {Show(row.DiffPercentGib),8}  " +
                (row.IsCompared
                    ? row.BcItems
                    : row.Status) +
                (row.BcLinesExcluded > 0
                    ? $" [{row.BcLinesExcluded} line(s) excluded]"
                    : string.Empty));
        }
    }

    private static string Show(decimal? value) =>
        value is null
            ? "unknown"
            : AcronisUsageDevCommand.Format(value.Value);
}
