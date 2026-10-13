
using System.Globalization;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Reporting;

/// <summary>
/// Exception-first compliance inventory presentation.
///
/// Suppresses reported-compliant devices unless their
/// matched setting evidence contradicts that status.
/// </summary>
public sealed class ComplianceDeviceListReporter
{
    private const int NameWidth = 32;
    private const int StateWidth = 15;
    private const int CheckInWidth = 16;
    private const int AgeWidth = 6;

    private const int TableWidth =
        NameWidth + StateWidth + CheckInWidth + AgeWidth + 6;

    public string Render(
        ComplianceDeviceListResult result,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var now = (timeProvider ?? TimeProvider.System)
            .GetUtcNow();

        var entries = result.Entries;

        var visible = entries
            .Where(ShouldShow)
            .OrderBy(entry => GetPriority(entry.Device.ComplianceState))
            .ThenBy(
                entry => entry.Device.DeviceName,
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                entry => entry.Device.Id,
                StringComparer.Ordinal
            )
            .ToList();

        var suppressed = entries.Count - visible.Count;

        var noncompliant = entries.Count(entry =>
            GetPriority(entry.Device.ComplianceState) == 0
        );

        var unknown = entries.Count(entry =>
            GetPriority(entry.Device.ComplianceState) == 1
        );

        var other = entries.Count(entry =>
            GetPriority(entry.Device.ComplianceState) == 2
        );

        var conflicts = visible.Count(HasConflict);

        using var writer = new StringWriter(
            CultureInfo.InvariantCulture
        );

        writer.WriteLine(
            "SUPPORTTOOLKIT // COMPLIANCE INVENTORY"
        );
        writer.WriteLine(
            "===================================="
        );
        writer.WriteLine("DEV PROXY - SYNTHETIC DATA");
        writer.WriteLine();

        writer.WriteLine(
            $"DEVICES: {entries.Count}   " +
            $"SHOWN: {visible.Count}   " +
            $"SUPPRESSED: {suppressed}"
        );

        writer.WriteLine(
            $"NONCOMPLIANT: {noncompliant}   " +
            $"UNKNOWN/MISSING: {unknown}   " +
            $"OTHER: {other}"
        );

        if (conflicts > 0)
        {
            writer.WriteLine(
                $"STATUS/SETTING CONFLICTS: {conflicts}"
            );
        }

        writer.WriteLine();

        WriteHeader(writer);

        var truncated = false;
        var futureCheckIn = false;

        foreach (var entry in visible)
        {
            var device = entry.Device;

            var name = Display(device.DeviceName);
            var state = Display(device.ComplianceState)
                .ToUpperInvariant();

            if (name.Length > NameWidth ||
                state.Length > StateWidth)
            {
                truncated = true;
            }

            if (device.LastSyncDateTime > now)
            {
                futureCheckIn = true;
            }

            writer.WriteLine(
                $"{Fit(name, NameWidth),-NameWidth}  " +
                $"{Fit(state, StateWidth),-StateWidth}  " +
                $"{FormatCheckIn(device.LastSyncDateTime),-CheckInWidth}  " +
                $"{FormatAge(device.LastSyncDateTime, now),-AgeWidth}"
            );

            WriteReasons(writer, entry);

            writer.WriteLine();
        }

        writer.WriteLine(new string('-', TableWidth));

        if (visible.Count == 0)
        {
            writer.WriteLine(
                entries.Count == 0
                    ? "No managed devices returned."
                    : "No device exceptions or evidence conflicts to display."
            );
        }

        writer.WriteLine();

        writer.WriteLine(
            $"{suppressed} reported-compliant " +
            (suppressed == 1 ? "device" : "devices") +
            " suppressed."
        );

        writer.WriteLine(
            $"All {entries.Count} device records accounted for."
        );

        writer.WriteLine();

        writer.WriteLine(
            "FAILED settings are candidate findings, not verified causes."
        );

        writer.WriteLine(
            "Setting/device ID correlation remains unverified."
        );

        writer.WriteLine(
            "No check-in staleness threshold has been applied."
        );

        if (truncated)
        {
            writer.WriteLine(
                "Some table values were shortened."
            );
        }

        if (futureCheckIn)
        {
            writer.WriteLine(
                "FUTURE indicates a check-in after report time."
            );
        }

        writer.WriteLine(
            "Synthetic data; tenant and policy coverage unverified."
        );

        return writer.ToString();
    }

    private static void WriteHeader(TextWriter writer)
    {
        writer.WriteLine(
            $"{"DEVICE",-NameWidth}  " +
            $"{"STATUS",-StateWidth}  " +
            $"{"LAST CHECK-IN",-CheckInWidth}  " +
            $"{"AGE",-AgeWidth}"
        );

        writer.WriteLine(new string('-', TableWidth));
    }

    private static void WriteReasons(
        TextWriter writer,
        ComplianceDeviceListEntry entry)
    {
        var settings = entry.Settings;

        var failed = settings
            .Where(IsFailure)
            .OrderBy(setting => setting.SettingName)
            .ToList();

        var unknown = settings
            .Where(IsUnknown)
            .OrderBy(setting => setting.SettingName)
            .ToList();

        var other = settings
            .Where(setting =>
                !IsFailure(setting)
                && !IsUnknown(setting)
                && !IsCompliant(setting))
            .OrderBy(setting => setting.SettingName)
            .ToList();

        foreach (var setting in failed)
        {
            writer.WriteLine(
                $"  FAILED:   {Display(setting.SettingName)}"
            );
        }

        foreach (var setting in unknown)
        {
            writer.WriteLine(
                $"  UNKNOWN:  {Display(setting.SettingName)}"
            );
        }

        foreach (var setting in other)
        {
            writer.WriteLine(
                $"  OTHER:    {Display(setting.SettingName)} " +
                $"({Display(setting.State)})"
            );
        }

        if (failed.Count == 0)
        {
            writer.WriteLine(
                settings.Count == 0
                    ? "  REASON:   No matching setting evidence."
                    : "  REASON:   No failed setting identified."
            );
        }

        if (HasConflict(entry))
        {
            writer.WriteLine(
                "  CONFLICT: Device reports compliant, " +
                "but a setting reports noncompliant."
            );
        }
    }

    private static bool ShouldShow(
        ComplianceDeviceListEntry entry)
    {
        return !IsReportedCompliant(entry.Device.ComplianceState)
            || HasConflict(entry);
    }

    private static bool HasConflict(
        ComplianceDeviceListEntry entry)
    {
        return IsReportedCompliant(entry.Device.ComplianceState)
            && entry.Settings.Any(IsFailure);
    }

    private static bool IsReportedCompliant(string? state)
    {
        return string.Equals(
            state?.Trim(),
            "compliant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsFailure(ComplianceSettingEvidence setting)
    {
        return string.Equals(
            setting.State?.Trim(),
            "nonCompliant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsUnknown(ComplianceSettingEvidence setting)
    {
        return string.IsNullOrWhiteSpace(setting.State)
            || string.Equals(
                setting.State.Trim(),
                "unknown",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsCompliant(ComplianceSettingEvidence setting)
    {
        return string.Equals(
            setting.State?.Trim(),
            "compliant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static int GetPriority(string? state)
    {
        if (string.Equals(
            state?.Trim(),
            "noncompliant",
            StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(state)
            || string.Equals(
                state.Trim(),
                "unknown",
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return IsReportedCompliant(state) ? 3 : 2;
    }

    private static string FormatCheckIn(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture
            )
            : "not provided";
    }

    private static string FormatAge(
        DateTimeOffset? value,
        DateTimeOffset now)
    {
        if (!value.HasValue)
        {
            return "--";
        }

        if (value.Value > now)
        {
            return "FUTURE";
        }

        var elapsed = now - value.Value;

        if (elapsed.TotalDays >= 1)
        {
            return $"{(int)Math.Floor(elapsed.TotalDays)}d";
        }

        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)Math.Floor(elapsed.TotalHours)}h";
        }

        return "<1h";
    }

    private static string Fit(string value, int width)
    {
        return value.Length <= width
            ? value
            : value[..(width - 3)] + "...";
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "not provided"
            : value.Trim();
    }
}
