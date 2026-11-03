using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Modules.UsageReport.Production;

/// <summary>
/// The Acronis metrics behind the three figures in the Acronis portal's
/// storage view, confirmed against the portal: Total = Backup +
/// Microsoft 365, to the byte.
/// </summary>
public static class AcronisStorageMetrics
{
    /// <summary>Portal: "Total storage".</summary>
    public const string Total = "storage_total";

    /// <summary>Portal: "Backup storage" (devices other than cloud apps).</summary>
    public const string Backup = "total_storage";

    /// <summary>Portal: "Microsoft 365 and Google Workspace backup".</summary>
    public const string Microsoft365 = "c2c_storage";
}

/// <summary>
/// Turns Acronis usage into the report's storage figures. All Acronis
/// knowledge of the report lives here: which metrics, how their lines add
/// up, and which units are accepted.
///
/// A metric is summed over its lines (one per edition and storage
/// location). No usage for the tenant, or no such metric, is "nothing
/// reported". A missing value or a unit other than bytes is unknown with
/// the reason. Neither is ever zero.
/// </summary>
public static class AcronisStorageReader
{
    /// <summary>
    /// What a reader of the report must know about Acronis figures. Always
    /// stated, never left out.
    /// </summary>
    public static IReadOnlyList<string> Caveats { get; } =
    [
        "Acronis describes these usage figures as refreshed every few " +
        "hours and not meant for billing.",
        "The Acronis portal shows binary GB (1,073,741,824 bytes), about 7% " +
        "lower than the decimal GB used here for the same data."
    ];

    public static UsageStorage Read(
        IReadOnlyCollection<string> requestedTenantIds,
        AcronisUsageResult usage)
    {
        ArgumentNullException.ThrowIfNull(requestedTenantIds);
        ArgumentNullException.ThrowIfNull(usage);

        var byTenant =
            requestedTenantIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    id => id,
                    id => new TenantStorage(
                        Measure(id, AcronisStorageMetrics.Total, usage),
                        Measure(id, AcronisStorageMetrics.Backup, usage),
                        Measure(id, AcronisStorageMetrics.Microsoft365, usage)),
                    StringComparer.OrdinalIgnoreCase);

        var notices = new List<string>(Caveats);

        if (usage.UnrequestedItems > 0)
        {
            notices.Add(
                $"Acronis returned usage for {usage.UnrequestedItems} " +
                "tenant(s) that were not asked for; it was ignored.");
        }

        return new UsageStorage(byTenant, PeriodStart(usage), notices);
    }

    /// <summary>
    /// The bytes of one metric for one tenant, or unknown with the reason.
    /// </summary>
    public static UsageMeasurement Measure(
        string tenantId,
        string metric,
        AcronisUsageResult usage)
    {
        if (!Guid.TryParse(tenantId, out var parsed)
            || !usage.ByTenant.TryGetValue(parsed.ToString("D"), out var measurements)
            || measurements.Count == 0)
        {
            return UsageMeasurement.NothingReported("no Acronis usage returned");
        }

        var matching =
            measurements
                .Where(item => string.Equals(
                    item.MetricName,
                    metric,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (matching.Length == 0)
        {
            return UsageMeasurement.NothingReported($"{metric} not reported");
        }

        if (matching.Any(item => item.Value is null))
        {
            return UsageMeasurement.Unknown($"a {metric} value is missing");
        }

        var wrongUnit =
            matching.FirstOrDefault(
                item => !string.Equals(
                    item.Unit,
                    "bytes",
                    StringComparison.OrdinalIgnoreCase));

        if (wrongUnit is not null)
        {
            return UsageMeasurement.Unknown(
                $"{metric} unit is {wrongUnit.Unit ?? "missing"}, not bytes");
        }

        return new UsageMeasurement(matching.Sum(item => item.Value!.Value), null);
    }

    private static string? PeriodStart(
        AcronisUsageResult usage)
    {
        var starts =
            usage.ByTenant.Values
                .SelectMany(rows => rows)
                .Select(row => row.RangeStart)
                .Where(start => !string.IsNullOrWhiteSpace(start))
                .Select(start => start!.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

        return starts.Length switch
        {
            0 => null,
            1 => starts[0],
            _ => $"{starts[0]} to {starts[^1]} (more than one period)"
        };
    }
}
