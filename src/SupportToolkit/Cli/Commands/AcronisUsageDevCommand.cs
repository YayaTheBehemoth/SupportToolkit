using System.Globalization;
using System.Text;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Cli.Commands;

public sealed record AcronisUsageOptions(
    bool ShowNames,
    string? OutputPath,
    IReadOnlyList<string>? Metrics = null,
    string? Tenant = null);

/// <summary>
/// One line of per-tenant usage, ready to join to other data. Status
/// says whether the row can be trusted; a tenant Acronis returned no
/// usage for is reported as unknown, never as zero.
/// </summary>
public sealed record AcronisUsageRow(
    string TenantId,
    string TenantName,
    string? ParentTenantId,
    string? ParentTenantName,
    string? Metric,
    string? Edition,
    string? Type,
    string? InfraId,
    string? ApplicationId,
    decimal? Value,
    decimal? AbsoluteValue,
    string? Unit,
    string? RangeStart,
    string Status);

/// <summary>
/// Developer-only, read-only diagnostic: what per-tenant usage does
/// Acronis report, and in which metrics and units.
///
/// It reads and prints. It does not compare usage with anything,
/// judge it, or feed any report.
///
/// The default output is aggregate and safe to share. --names adds
/// one row per customer tenant, which contains customer names; with
/// --out it is written to a file, never inside a git working tree.
///
/// The CLI dispatcher must require an explicit --production argument.
/// </summary>
public sealed class AcronisUsageDevCommand
{
    private const string Unknown = "unknown";

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public AcronisUsageDevCommand(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));
    }

    /// <summary>
    /// Parses the arguments after "dev acronis-usage": --production,
    /// then optionally --names and, with it, --out &lt;path&gt;.
    /// </summary>
    public static bool TryParseArguments(
        IReadOnlyList<string> args,
        out AcronisUsageOptions? options,
        out string error)
    {
        options = null;
        error =
            "Usage: SupportToolkit dev acronis-usage --production " +
            "[--metric <name>]... [--tenant <text>] " +
            "[--names [--out <path>]]";

        if (args.Count == 0
            || !string.Equals(
                args[0],
                "--production",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var showNames = false;
        string? outputPath = null;
        var metrics = new List<string>();
        string? tenant = null;

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
                         "--out",
                         StringComparison.OrdinalIgnoreCase)
                     && outputPath is null
                     && index + 1 < args.Count
                     && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                outputPath = args[++index];
            }
            else if (string.Equals(
                         args[index],
                         "--metric",
                         StringComparison.OrdinalIgnoreCase)
                     && index + 1 < args.Count
                     && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                metrics.Add(args[++index].Trim());
            }
            else if (string.Equals(
                         args[index],
                         "--tenant",
                         StringComparison.OrdinalIgnoreCase)
                     && tenant is null
                     && index + 1 < args.Count
                     && !string.IsNullOrWhiteSpace(args[index + 1]))
            {
                tenant = args[++index].Trim();
            }
            else
            {
                return false;
            }
        }

        if (outputPath is not null && !showNames)
        {
            error = "--out needs --names: only the per-tenant rows " +
                    "are written to a file.";
            return false;
        }

        options = new AcronisUsageOptions(
                showNames,
                outputPath,
                metrics,
                tenant);
        error = string.Empty;

        return true;
    }

    public async Task<int> RunAsync(
        TextWriter output,
        AcronisUsageOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);

        // An unsafe path is refused before anything is read.
        string? fullOutputPath = null;

        if (options.OutputPath is not null
            && !TryValidateOutputPath(
                options.OutputPath,
                out fullOutputPath,
                out var pathError))
        {
            await Console.Error.WriteLineAsync(pathError);
            return 1;
        }

        using var session =
            await AcronisDevSession.CreateAsync(
                _configurationResolver,
                cancellationToken);

        var tenants =
            await session.Provider.GetTenantsAsync(cancellationToken);

        var requestIds =
            CustomerTenants(tenants)
                .Where(tenant => Guid.TryParse(tenant.Id, out _))
                .Select(tenant => tenant.Id)
                .ToArray();

        var usage =
            await session.UsageProvider.GetUsagesAsync(
                requestIds,
                cancellationToken);

        return WriteReport(
            output,
            tenants,
            usage,
            options.ShowNames,
            fullOutputPath,
            options.Metrics,
            options.Tenant);
    }

    /// <summary>
    /// Writes the aggregate report, then (with names) the per-tenant
    /// rows to the output or, when a path is given, to that file.
    /// The path must already have passed TryValidateOutputPath.
    /// </summary>
    public static int WriteReport(
        TextWriter output,
        IReadOnlyList<TenantDto> tenants,
        AcronisUsageResult usage,
        bool showNames,
        string? validatedOutputPath = null,
        IReadOnlyList<string>? metrics = null,
        string? tenantFilter = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(usage);

        var rows = BuildRows(tenants, usage, metrics);

        if (tenantFilter is not null)
        {
            rows =
                rows
                    .Where(
                        row =>
                            string.Equals(
                                row.TenantId,
                                tenantFilter,
                                StringComparison.OrdinalIgnoreCase)
                            || row.TenantName.Contains(
                                tenantFilter,
                                StringComparison.OrdinalIgnoreCase))
                    .ToArray();

            if (!showNames)
            {
                return WriteTenantTable(output, rows);
            }
        }

        var incomplete =
            WriteAggregate(output, tenants, usage, rows);

        if (!showNames)
        {
            output.WriteLine();
            output.WriteLine(
                "Customer names hidden. Use --names for one row per " +
                "tenant (contains customer names).");

            return incomplete ? 2 : 0;
        }

        output.WriteLine();

        if (validatedOutputPath is null)
        {
            output.WriteLine("PER-TENANT ROWS (contain customer names)");
            output.WriteLine("----------------------------------------");
            WriteCsv(output, rows);
        }
        else
        {
            using (var stream =
                   new FileStream(
                       validatedOutputPath,
                       FileMode.CreateNew,
                       FileAccess.Write))
            using (var writer =
                   new StreamWriter(
                       stream,
                       new UTF8Encoding(false)))
            {
                WriteCsv(writer, rows);
            }

            output.WriteLine(
                $"Wrote {rows.Count} row(s) to {validatedOutputPath}.");
            output.WriteLine(
                "The file contains customer names. Keep it out of the " +
                "repository and do not share it publicly.");
        }

        return incomplete ? 2 : 0;
    }

    /// <summary>
    /// Console table for the tenants a filter matched: nonzero rows
    /// only, with byte values shown raw next to decimal GB (10^9) and
    /// binary GiB (2^30) so the matching convention can be seen.
    /// The raw value stays the reported one; the conversions are
    /// labelled helpers for comparing with another screen.
    /// </summary>
    private static int WriteTenantTable(
        TextWriter output,
        IReadOnlyList<AcronisUsageRow> rows)
    {
        output.WriteLine();
        output.WriteLine("ACRONIS USAGE FOR MATCHED TENANT(S) (read-only)");
        output.WriteLine("-----------------------------------------------");

        var matched =
            rows.Select(row => (row.TenantId, row.TenantName))
                .Distinct()
                .ToArray();

        if (matched.Length == 0)
        {
            output.WriteLine("No customer tenant matched the filter.");
            return 2;
        }

        var incomplete = false;

        foreach (var (tenantId, tenantName) in matched)
        {
            var own =
                rows.Where(row => row.TenantId == tenantId).ToArray();

            var hiddenZero =
                own.Count(row => row.Status == "ok" && row.Value == 0m);

            var shown =
                own.Where(row => row.Status != "ok" || row.Value != 0m)
                    .OrderBy(
                        row => row.Metric,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        row => row.Edition,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            output.WriteLine();
            output.WriteLine($"{tenantName} ({tenantId})");
            output.WriteLine(
                $"  {"metric",-34} {"edition",-18} {"type",-8} " +
                $"{"raw value",18} {"unit",-9} {"GB (1e9)",12} " +
                $"{"GiB (2^30)",12} {"abs raw",18}");

            foreach (var row in shown)
            {
                if (row.Status != "ok")
                {
                    incomplete = true;
                }

                var isBytes =
                    string.Equals(
                        row.Unit,
                        "bytes",
                        StringComparison.OrdinalIgnoreCase)
                    && row.Value is not null;

                output.WriteLine(
                    $"  {row.Metric ?? "<missing>",-34} " +
                    $"{row.Edition ?? "<none>",-18} " +
                    $"{row.Type ?? "<none>",-8} " +
                    $"{Number(row.Value),18} " +
                    $"{row.Unit ?? Unknown,-9} " +
                    $"{(isBytes ? Format(row.Value!.Value / 1_000_000_000m) : "-"),12} " +
                    $"{(isBytes ? Format(row.Value!.Value / 1_073_741_824m) : "-"),12} " +
                    $"{Number(row.AbsoluteValue),18}" +
                    (row.Status == "ok" ? string.Empty : $"  [{row.Status}]"));
            }

            output.WriteLine(
                $"  ({hiddenZero} zero-valued row(s) hidden.)");
        }

        output.WriteLine();
        output.WriteLine(
            "Raw values are exactly as Acronis reports them; GB and " +
            "GiB are helper conversions for comparing with another " +
            "screen. This output contains a customer name.");

        return incomplete ? 2 : 0;
    }

    /// <summary>
    /// One row per customer tenant and measurement. A tenant with no
    /// usage returned, or with an invalid id, gets one explicit
    /// unknown row so it cannot disappear from a join.
    /// </summary>
    public static IReadOnlyList<AcronisUsageRow> BuildRows(
        IReadOnlyList<TenantDto> tenants,
        AcronisUsageResult usage,
        IReadOnlyList<string>? metrics = null)
    {
        var names =
            tenants
                .Where(tenant => !string.IsNullOrWhiteSpace(tenant.Id))
                .GroupBy(
                    tenant => tenant.Id.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

        var rows = new List<AcronisUsageRow>();

        foreach (var tenant in CustomerTenants(tenants)
                     .OrderBy(
                         tenant => tenant.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         tenant => tenant.Id,
                         StringComparer.OrdinalIgnoreCase))
        {
            var parentName =
                tenant.ParentId is not null
                && names.TryGetValue(tenant.ParentId.Trim(), out var name)
                    ? name
                    : null;

            AcronisUsageRow UnknownRow(string status) =>
                new(
                    tenant.Id,
                    tenant.Name,
                    tenant.ParentId,
                    parentName,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    status);

            if (!Guid.TryParse(tenant.Id, out var parsed))
            {
                rows.Add(
                    UnknownRow(
                        "unknown: invalid tenant id, usage not requested"));
                continue;
            }

            if (!usage.ByTenant.TryGetValue(
                    parsed.ToString("D"),
                    out var measurements)
                || measurements.Count == 0)
            {
                rows.Add(UnknownRow("unknown: no usage returned"));
                continue;
            }

            IReadOnlyList<AcronisTenantUsage> selected =
                metrics is null || metrics.Count == 0
                    ? measurements
                    : measurements
                        .Where(
                            item => metrics.Contains(
                                item.MetricName ?? string.Empty,
                                StringComparer.OrdinalIgnoreCase))
                        .ToArray();

            if (selected.Count == 0)
            {
                rows.Add(
                    UnknownRow(
                        "unknown: requested metric not reported"));
                continue;
            }

            foreach (var measurement in selected
                         .OrderBy(
                             item => item.MetricName,
                             StringComparer.OrdinalIgnoreCase)
                         .ThenBy(
                             item => item.Edition,
                             StringComparer.OrdinalIgnoreCase))
            {
                var problems = new List<string>();

                if (measurement.Value is null)
                {
                    problems.Add("value missing");
                }

                if (string.IsNullOrWhiteSpace(measurement.Unit))
                {
                    problems.Add("unit missing");
                }

                if (string.IsNullOrWhiteSpace(measurement.MetricName))
                {
                    problems.Add("metric name missing");
                }

                rows.Add(
                    new AcronisUsageRow(
                        tenant.Id,
                        tenant.Name,
                        tenant.ParentId,
                        parentName,
                        measurement.MetricName,
                        measurement.Edition,
                        measurement.Type,
                        measurement.InfraId,
                        measurement.ApplicationId,
                        measurement.Value,
                        measurement.AbsoluteValue,
                        measurement.Unit,
                        measurement.RangeStart,
                        problems.Count == 0
                            ? "ok"
                            : "unknown: " + string.Join("; ", problems)));
            }
        }

        return rows;
    }

    /// <summary>
    /// Refuses a path that is a directory, in a missing directory,
    /// inside any git working tree (this repository is public), or
    /// already a file.
    /// </summary>
    public static bool TryValidateOutputPath(
        string path,
        out string fullPath,
        out string error)
    {
        fullPath = string.Empty;
        error = string.Empty;

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException
                  or NotSupportedException
                  or PathTooLongException)
        {
            error = "The output path is not valid.";
            return false;
        }

        if (Directory.Exists(fullPath))
        {
            error = "The output path is a directory; give a file path.";
            return false;
        }

        var directory = Path.GetDirectoryName(fullPath);

        if (directory is null || !Directory.Exists(directory))
        {
            error =
                "The output directory does not exist. It is not " +
                "created for you.";
            return false;
        }

        for (var ancestor = directory;
             ancestor is not null;
             ancestor = Path.GetDirectoryName(ancestor))
        {
            var marker = Path.Combine(ancestor, ".git");

            if (Directory.Exists(marker) || File.Exists(marker))
            {
                error =
                    "Refusing to write inside a git working tree: " +
                    "the output contains customer names and this " +
                    "repository is public.";
                return false;
            }
        }

        if (File.Exists(fullPath))
        {
            error = "The output file already exists; it is not " +
                    "overwritten.";
            return false;
        }

        return true;
    }

    private static IEnumerable<TenantDto> CustomerTenants(
        IReadOnlyList<TenantDto> tenants) =>
        tenants.Where(
            tenant =>
                tenant is not null
                && string.Equals(
                    tenant.Kind,
                    "customer",
                    StringComparison.OrdinalIgnoreCase));

    private static bool WriteAggregate(
        TextWriter output,
        IReadOnlyList<TenantDto> tenants,
        AcronisUsageResult usage,
        IReadOnlyList<AcronisUsageRow> rows)
    {
        var customers =
            CustomerTenants(tenants).ToArray();

        var measured =
            rows.Where(row => row.Metric is not null).ToArray();

        var tenantsWithUsage =
            measured.Select(row => row.TenantId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        var invalidIds =
            customers.Count(tenant => !Guid.TryParse(tenant.Id, out _));

        var withoutUsage =
            rows.Count(row => row.Metric is null
                && row.Status.StartsWith(
                    "unknown: ",
                    StringComparison.Ordinal)
                && !row.Status.StartsWith(
                    "unknown: invalid",
                    StringComparison.Ordinal));

        var rowsWithProblems =
            measured.Count(row => row.Status != "ok");

        output.WriteLine();
        output.WriteLine("ACRONIS USAGE PROBE (read-only)");
        output.WriteLine("-------------------------------");
        output.WriteLine($"Customer tenants:              {customers.Length}");
        output.WriteLine($"  With usage returned:         {tenantsWithUsage}");
        output.WriteLine($"  No usage returned (unknown): {withoutUsage}");
        output.WriteLine($"  Invalid tenant id (unknown): {invalidIds}");
        output.WriteLine($"Usage rows:                    {measured.Length}");
        output.WriteLine($"  Rows with a missing value, unit or name: {rowsWithProblems}");
        output.WriteLine($"Items for tenants not requested: {usage.UnrequestedItems}");

        output.WriteLine();
        output.WriteLine("Metrics and units seen (as Acronis reports them):");

        if (measured.Length == 0)
        {
            output.WriteLine("  None.");
        }

        foreach (var group in measured
                     .GroupBy(
                         row => (
                             Metric: row.Metric ?? "<name missing>",
                             Unit: string.IsNullOrWhiteSpace(row.Unit)
                                 ? "<unit missing>"
                                 : row.Unit!),
                         row => row)
                     .OrderBy(
                         group => group.Key.Metric,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         group => group.Key.Unit,
                         StringComparer.OrdinalIgnoreCase))
        {
            var values =
                group.Where(row => row.Value is not null)
                    .Select(row => row.Value!.Value)
                    .Order()
                    .ToArray();

            var editions =
                group.Select(row => row.Edition ?? "<none>")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase);

            output.WriteLine(
                $"  {group.Key.Metric} [{group.Key.Unit}] " +
                $"editions: {string.Join(", ", editions)}");

            output.WriteLine(
                $"    rows: {group.Count()}, tenants: " +
                $"{group.Select(row => row.TenantId).Distinct(StringComparer.OrdinalIgnoreCase).Count()}, " +
                $"values missing (unknown): {group.Count() - values.Length}");

            if (values.Length > 0)
            {
                output.WriteLine(
                    $"    min {Format(values[0])}, " +
                    $"median {Format(Median(values))}, " +
                    $"p90 {Format(Percentile(values, 0.9m))}, " +
                    $"max {Format(values[^1])}");
            }
        }

        output.WriteLine();
        output.WriteLine(
            "Units are shown exactly as Acronis reports them and are " +
            "never converted (bytes are not GB, and GB is not GiB).");
        output.WriteLine(
            "No measurement timestamp is returned; range_start is the " +
            "start of the usage period. Acronis documents these " +
            "figures as refreshed every few hours and not as a " +
            "billing source.");

        return withoutUsage > 0
               || invalidIds > 0
               || rowsWithProblems > 0
               || usage.UnrequestedItems > 0;
    }

    private static void WriteCsv(
        TextWriter writer,
        IReadOnlyList<AcronisUsageRow> rows)
    {
        writer.WriteLine(
            "tenant_id,tenant_name,parent_tenant_id,parent_tenant_name," +
            "metric,edition,type,infra_id,application_id,value," +
            "absolute_value,unit,range_start,status");

        foreach (var row in rows)
        {
            writer.WriteLine(
                string.Join(
                    ",",
                    Csv(row.TenantId),
                    Csv(row.TenantName),
                    Csv(row.ParentTenantId),
                    Csv(row.ParentTenantName),
                    Csv(row.Metric),
                    Csv(row.Edition),
                    Csv(row.Type),
                    Csv(row.InfraId),
                    Csv(row.ApplicationId),
                    Number(row.Value),
                    Number(row.AbsoluteValue),
                    Csv(row.Unit),
                    Csv(row.RangeStart),
                    Csv(row.Status)));
        }
    }

    /// <summary>
    /// Quotes a field and neutralizes spreadsheet formulas, because
    /// tenant names are not trusted text.
    /// </summary>
    internal static string Csv(
        string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text =
            "=+-@\t\r".Contains(value[0])
                ? "'" + value
                : value;

        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    internal static string Number(
        decimal? value) =>
        value is null
            ? Unknown
            : value.Value.ToString("G29", CultureInfo.InvariantCulture);

    internal static string Format(
        decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static decimal Median(
        decimal[] sorted) =>
        sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2])
              / 2;

    /// <summary>
    /// Nearest-rank percentile of an ascending array.
    /// </summary>
    internal static decimal Percentile(
        decimal[] sorted,
        decimal fraction)
    {
        var rank =
            (int)Math.Ceiling(fraction * sorted.Length);

        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }
}
