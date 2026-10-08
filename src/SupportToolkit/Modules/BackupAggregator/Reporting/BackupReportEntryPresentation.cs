using System.Globalization;
using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

internal static class BackupReportEntryPresentation
{
    public static string GetDisplayStatus(
        BackupReportEntry entry)
    {
        var state =
            entry.DeviceState;

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

            return "Idle";
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
        if (string.IsNullOrWhiteSpace(entry.DeviceName)
            || entry.DeviceName.StartsWith(
                "<unknown ",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return "Resource identity missing";
        }

        if (string.IsNullOrWhiteSpace(entry.DeviceState))
        {
            return "Resource state missing";
        }

        if (string.Equals(
                entry.DeviceState,
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
