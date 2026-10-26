using System.Text;

namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Item number comparison for subscription lines.
///
/// Item numbers may contain spaces and non-ASCII letters. Values are
/// trimmed and normalized to Unicode form C before an ordinal,
/// case-insensitive comparison, so a precomposed and a decomposed
/// spelling of the same number are equal. Internal spaces are kept.
/// </summary>
public static class SubscriptionItemNumbers
{
    /// <summary>
    /// Provisional entitlement list, pending business sign-off.
    /// </summary>
    public static IReadOnlyList<string> DefaultMonitoringItemNumbers { get; } =
        ["BAASOVERVÅG"];

    public static IEqualityComparer<string> Comparer { get; } =
        new ItemNumberComparer();

    public static string Normalize(
        string? itemNumber)
    {
        return (itemNumber ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormC);
    }

    public static HashSet<string> ToSet(
        IEnumerable<string> itemNumbers)
    {
        ArgumentNullException.ThrowIfNull(itemNumbers);

        return itemNumbers
            .Select(Normalize)
            .Where(number => number.Length > 0)
            .ToHashSet(Comparer);
    }

    private sealed class ItemNumberComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) =>
            string.Equals(
                Normalize(x),
                Normalize(y),
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(string value) =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(value));
    }
}
