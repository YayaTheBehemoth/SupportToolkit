using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Aggregate-only analysis of raw subscription data, for validating
/// the contract assumptions against a real read.
///
/// Prints counts and the distinct values of enum-like columns only.
/// It never prints customer numbers, names, subscription numbers,
/// company names or any other row-level value, so the output is
/// suitable for sharing.
/// </summary>
public static class BusinessCentralSubscriptionDiagnostics
{
    private const int MaxDistinctValues = 20;
    private const int MaxValueLength = 40;

    public static void Write(
        TextWriter output,
        BusinessCentralSubscriptionData data,
        string? monitoringItemNumber,
        string? expectedEnvironment,
        string? expectedCompanyName)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);

        var headers = data.Headers.Where(h => h is not null).Select(h => h!).ToList();
        var lines = data.Lines.Where(l => l is not null).Select(l => l!).ToList();

        output.WriteLine("BUSINESS CENTRAL SUBSCRIPTION DIAGNOSTICS");
        output.WriteLine("-----------------------------------------");
        output.WriteLine("Read-only. Counts and enum-like values only.");
        output.WriteLine();
        output.WriteLine($"Headers: {data.Headers.Count} ({data.Headers.Count - headers.Count} empty records)");
        output.WriteLine($"Lines:   {data.Lines.Count} ({data.Lines.Count - lines.Count} empty records)");

        output.WriteLine();
        output.WriteLine("Header status (as received; language dependent):");
        WriteCounts(output, headers.Select(h => Display(h.Status)));

        output.WriteLine();
        output.WriteLine("Header blocked (decoded):");
        WriteCounts(output, headers.Select(h => Display(BusinessCentralEnumNames.Decode(h.Blocked))));

        output.WriteLine();
        output.WriteLine("Header type (decoded):");
        WriteCounts(output, headers.Select(h => Display(BusinessCentralEnumNames.Decode(h.Type))));

        output.WriteLine();
        output.WriteLine("Header end date:");
        WriteCounts(output, headers.Select(h => ClassifyDate(h.EndDate)));

        output.WriteLine();
        output.WriteLine("Line type (decoded):");
        WriteCounts(output, lines.Select(l => Display(BusinessCentralEnumNames.Decode(l.Type))));

        output.WriteLine();
        output.WriteLine("Line omitLine:");
        WriteCounts(output, lines.Select(l => l.OmitLine?.ToString().ToLowerInvariant() ?? "(null)"));

        output.WriteLine();
        output.WriteLine("Line company / environment columns:");
        WriteContext(output, lines, expectedEnvironment, expectedCompanyName);

        output.WriteLine();
        output.WriteLine("Join integrity (subscriptionNumber -> number):");
        WriteJoin(output, headers, lines);

        output.WriteLine();
        WriteMonitoringItem(output, lines, monitoringItemNumber);
    }

    private static void WriteContext(
        TextWriter output,
        IReadOnlyList<BusinessCentralSubscriptionLineDto> lines,
        string? expectedEnvironment,
        string? expectedCompanyName)
    {
        var populatedEnvironment =
            lines.Count(l => !string.IsNullOrWhiteSpace(l.Environment));

        var populatedCompany =
            lines.Count(l => !string.IsNullOrWhiteSpace(l.Company));

        output.WriteLine($"  environment populated: {populatedEnvironment} of {lines.Count}");
        output.WriteLine($"  company populated:     {populatedCompany} of {lines.Count}");

        output.WriteLine(
            "  environment conflicting: " +
            lines.Count(l => Conflicts(l.Environment, expectedEnvironment)));

        output.WriteLine(
            "  company conflicting:     " +
            lines.Count(l => Conflicts(l.Company, expectedCompanyName)));
    }

    private static void WriteJoin(
        TextWriter output,
        IReadOnlyList<BusinessCentralSubscriptionHeaderDto> headers,
        IReadOnlyList<BusinessCentralSubscriptionLineDto> lines)
    {
        var headerNumbers =
            headers
                .Select(h => h.Number?.Trim() ?? string.Empty)
                .ToList();

        var duplicateHeaders =
            headerNumbers
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1);

        var known =
            headerNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphanLines =
            lines.Count(
                l => !known.Contains(l.SubscriptionNumber?.Trim() ?? string.Empty));

        var duplicateLineKeys =
            lines
                .GroupBy(
                    l => (
                        (l.SubscriptionNumber?.Trim() ?? string.Empty).ToUpperInvariant(),
                        l.LineNumber))
                .Count(g => g.Count() > 1);

        var referenced =
            lines
                .Select(l => l.SubscriptionNumber?.Trim() ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        output.WriteLine($"  header numbers shared by more than one header: {duplicateHeaders}");
        output.WriteLine($"  (subscriptionNumber, lineNumber) duplicated:    {duplicateLineKeys}");
        output.WriteLine($"  lines without a matching header:                {orphanLines}");
        output.WriteLine($"  headers without any line:                       {headerNumbers.Count(n => !referenced.Contains(n))}");
    }

    private static void WriteMonitoringItem(
        TextWriter output,
        IReadOnlyList<BusinessCentralSubscriptionLineDto> lines,
        string? monitoringItemNumber)
    {
        output.WriteLine("Configured monitoring item:");

        if (string.IsNullOrWhiteSpace(monitoringItemNumber))
        {
            output.WriteLine("  not configured for this profile");
            return;
        }

        var matching =
            lines
                .Where(
                    l => string.Equals(
                        l.Number?.Trim(),
                        monitoringItemNumber.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        output.WriteLine($"  lines carrying the item number: {matching.Count}");

        output.WriteLine("  by line type (decoded):");
        WriteCounts(
            output,
            matching.Select(l => Display(BusinessCentralEnumNames.Decode(l.Type))),
            indent: "    ");

        output.WriteLine(
            "  subscriptions with more than one such line: " +
            matching
                .GroupBy(
                    l => l.SubscriptionNumber?.Trim() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
                .Count(g => g.Count() > 1));
    }

    private static void WriteCounts(
        TextWriter output,
        IEnumerable<string> values,
        string indent = "  ")
    {
        var groups =
            values
                .GroupBy(v => v, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

        if (groups.Count == 0)
        {
            output.WriteLine($"{indent}(none)");
            return;
        }

        foreach (var group in groups.Take(MaxDistinctValues))
        {
            output.WriteLine($"{indent}{group.Key} x {group.Count()}");
        }

        if (groups.Count > MaxDistinctValues)
        {
            output.WriteLine(
                $"{indent}... {groups.Count - MaxDistinctValues} more distinct value(s)");
        }
    }

    private static string Display(
        string? value)
    {
        if (value is null)
        {
            return "(null)";
        }

        var cleaned =
            new string(
                value
                    .Where(c => !char.IsControl(c))
                    .Take(MaxValueLength)
                    .ToArray());

        return cleaned.Length == 0
            ? "(empty)"
            : cleaned.Trim().Length == 0
                ? "(blank)"
                : $"'{cleaned}'";
    }

    private static string ClassifyDate(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "missing";
        }

        return raw.Trim() is "0001-01-01" or "0000-12-31"
            ? "empty-date sentinel"
            : "dated";
    }

    private static bool Conflicts(
        string? reported,
        string? expected)
    {
        if (string.IsNullOrWhiteSpace(reported))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(expected)
            || !string.Equals(
                reported.Trim(),
                expected.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }
}
