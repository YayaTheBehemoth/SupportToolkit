using System.Globalization;
using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

internal static class BackupReportEntryPresentation
{
    public static string GetDisplayStatus(
        BackupReportEntry entry)
    {
        var state =
            entry.ResourceState;

        var result =
            entry.LastResult;

        if (string.Equals(
                state,
                "notProtected",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                state,
                "not_protected",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Not protected";
        }

        if (string.Equals(
                state,
                "idle",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            if (string.Equals(
                    result,
                    "ok",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Healthy";
            }

            /*
             * Device resources commonly use "idle" as both their state and
             * result. Keep the existing human-readable Idle presentation for
             * those resources.
             *
             * Microsoft 365 findings can also be idle while SupportToolkit
             * has derived a more useful review reason such as
             * protection_conflict, not_protected, or no_successful_backup.
             * In those cases the derived result is more informative than the
             * transport state and should be shown instead.
             */
            if (IsMissing(result)
                || string.Equals(
                    result,
                    "idle",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Idle";
            }

            return ToDisplayText(
                result
            );
        }

        if (string.Equals(
                state,
                "completed",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            if (string.Equals(
                    result,
                    "ok",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Successful";
            }

            if (string.Equals(
                    result,
                    "warning",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return "Completed with warnings";
            }

            if (IsMissing(result))
            {
                return "Completed - result unknown";
            }

            return $"Completed - {ToDisplayText(result)}";
        }

        if (string.Equals(
                state,
                "running",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                state,
                "backup",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "In progress";
        }

        if (string.Equals(
                state,
                "waiting",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Waiting";
        }

        return ToDisplayText(
            state ?? "Unknown"
        );
    }

    public static string GetUnclassifiedReason(
        BackupReportEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ResourceName)
            || entry.ResourceName.StartsWith(
                "<unknown ",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Resource identity missing";
        }

        if (string.IsNullOrWhiteSpace(entry.ResourceState))
        {
            return "Resource state missing";
        }

        if (string.Equals(
                entry.ResourceState,
                "completed",
                StringComparison.OrdinalIgnoreCase
            )
            && IsMissing(entry.LastResult))
        {
            return "Result code missing";
        }

        return "Resource data not recognized";
    }

    public static string FormatTimestamp(
        DateTimeOffset? timestamp)
    {
        return timestamp is null
            ? "-"
            : timestamp.Value
                .ToUniversalTime()
                .ToString(
                    "yyyy-MM-dd HH:mm",
                    CultureInfo.InvariantCulture
                );
    }

    public static string Fit(
        string? value,
        int width)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        if (value.Length <= width)
        {
            return value;
        }

        if (width <= 3)
        {
            return value[..width];
        }

        return value[..(width - 3)]
               + "...";
    }

    private static bool IsMissing(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(
                   value,
                   "<missing>",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static string ToDisplayText(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var normalized =
            value
                .Replace(
                    '_',
                    ' '
                )
                .Trim()
                .ToLowerInvariant();

        if (normalized.Length == 0)
        {
            return "Unknown";
        }

        return char.ToUpper(
                   normalized[0],
                   CultureInfo.InvariantCulture
               )
               + normalized[1..];
    }
}