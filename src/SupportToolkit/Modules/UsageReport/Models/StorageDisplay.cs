using System.Globalization;

namespace SupportToolkit.Modules.UsageReport.Models;

/// <summary>
/// A storage figure as the backup system's own screens show it, so it can
/// be compared with them by eye. Comparisons stay in GB.
///
/// Some portals show GB up to 1,024 GB and TB from there, with two
/// decimals, counting 1 TB as 1,024 GB (observed for the first backup
/// system, November 27, 2026). Their TB figure is rounded: two decimals of
/// a TB hide up to about 5 GB, which is why the exact GB is shown with it.
/// </summary>
public static class StorageDisplay
{
    public const decimal GbPerTb = 1024m;

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// True when the figure is shown in TB: the system shows TB, and the
    /// figure is 1,024 GB or more.
    /// </summary>
    public static bool InTerabytes(decimal gb, bool systemShowsTerabytes) =>
        systemShowsTerabytes && gb >= GbPerTb;

    /// <summary>
    /// "3.35 TB" or "841.20 GB": the figure as the system shows it, rounded
    /// to two decimals the way the portal rounds (half away from zero).
    /// </summary>
    public static string AsShown(decimal gb, bool systemShowsTerabytes) =>
        InTerabytes(gb, systemShowsTerabytes)
            ? Math.Round(gb / GbPerTb, 2, MidpointRounding.AwayFromZero)
                  .ToString("#,##0.00", Invariant) + " TB"
            : Math.Round(gb, 2, MidpointRounding.AwayFromZero)
                  .ToString("#,##0.00", Invariant) + " GB";
}
