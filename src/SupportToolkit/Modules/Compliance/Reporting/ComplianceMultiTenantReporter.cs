
using System.Globalization;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Reporting;

/// <summary>
/// Consolidated, exception-first synthetic tenant report.
///
/// Collection success is distinct from data completeness.
/// No case, ticket, remediation or recovery decision is made.
/// </summary>
public sealed class ComplianceMultiTenantReporter
{
    public string Render(
        ComplianceMultiTenantReport report,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        var now = (timeProvider ?? TimeProvider.System)
            .GetUtcNow();

        var successful = report.Tenants
            .Where(tenant => tenant.Data is not null)
            .ToList();

        var observed = successful.Sum(tenant =>
            tenant.Data!.Entries.Count
        );

        var visibleCount = successful.Sum(tenant =>
            tenant.Data!.Entries.Count(ShouldShow)
        );

        var suppressed = observed - visibleCount;

        var unmatched = successful.Sum(tenant =>
            tenant.Data!.UnmatchedSettings.Count
        );

        using var writer = new StringWriter(
            CultureInfo.InvariantCulture
        );

        writer.WriteLine(
            "SUPPORTTOOLKIT // COMPLIANCE REPORT"
        );
        writer.WriteLine(
            "=================================="
        );

        writer.WriteLine(
            "SYNTHETIC TENANTS - NO LIVE GRAPH OR BC"
        );

        writer.WriteLine(
            "Generated: " + FormatDate(now)
        );

        writer.WriteLine();

        writer.WriteLine(
            report.AllSelectedCollectionsSucceeded
                ? "COLLECTION: ALL SELECTED SOURCES RESPONDED"
                : "COLLECTION: PARTIAL - TENANT FAILURES"
        );

        writer.WriteLine(
            $"Tenants: {report.Tenants.Count} selected / " +
            $"{report.SuccessfulTenants} collected / " +
            $"{report.FailedTenants} failed"
        );

        writer.WriteLine(
            $"Observed devices: {observed}   " +
            $"Shown: {visibleCount}   " +
            $"Suppressed: {suppressed}"
        );

        writer.WriteLine(
            $"Unmatched setting observations: {unmatched}"
        );

        if (report.FailedTenants > 0)
        {
            writer.WriteLine();
            writer.WriteLine(
                "WARNING: Failed tenants are excluded " +
                "from observed totals."
            );
            writer.WriteLine(
                "Their device counts and compliance findings " +
                "are UNAVAILABLE, not zero."
            );
        }

        foreach (var tenant in report.Tenants)
        {
            writer.WriteLine();
            writer.WriteLine(
                new string('=', 68)
            );

            writer.WriteLine(
                $"TENANT: {tenant.Scope.DisplayName} " +
                $"[{tenant.Scope.Key}]"
            );

            writer.WriteLine(
                new string('-', 68)
            );

            if (tenant.Data is null)
            {
                writer.WriteLine(
                    "  COLLECTION FAILED: " +
                    (tenant.FailureReason ?? "Unknown failure")
                );

                writer.WriteLine(
                    "  Device inventory and setting evidence unavailable."
                );

                writer.WriteLine(
                    "  No compliance conclusion can be drawn."
                );

                continue;
            }

            WriteCollectedTenant(
                writer,
                tenant.Data,
                now
            );
        }

        writer.WriteLine();
        writer.WriteLine(
            new string('=', 68)
        );

        writer.WriteLine(
            "REPORT LIMITATIONS"
        );

        writer.WriteLine(
            "- Collected counts cover selected synthetic sources only."
        );

        writer.WriteLine(
            "- Full Intune policy and device coverage is unverified."
        );

        writer.WriteLine(
            "- Device-to-setting ID correlation is not live-validated."
        );

        writer.WriteLine(
            "- Last check-in age is not a compliance verdict."
        );

        writer.WriteLine(
            "- An absent device does not establish recovery."
        );

        writer.WriteLine(
            "- No Zendesk or compliance lifecycle action performed."
        );

        return writer.ToString();
    }

    private static void WriteCollectedTenant(
        TextWriter writer,
        ComplianceDeviceListResult data,
        DateTimeOffset now)
    {
        var visible = data.Entries
            .Where(ShouldShow)
            .OrderBy(entry =>
                GetPriority(entry.Device.ComplianceState))
            .ThenBy(
                entry => entry.Device.DeviceName,
                StringComparer.OrdinalIgnoreCase
            )
            .ThenBy(
                entry => entry.Device.Id,
                StringComparer.Ordinal
            )
            .ToList();

        var suppressed = data.Entries.Count - visible.Count;

        var noncompliant = data.Entries.Count(entry =>
            GetPriority(entry.Device.ComplianceState) == 0
        );

        var unknown = data.Entries.Count(entry =>
            GetPriority(entry.Device.ComplianceState) == 1
        );

        var conflicts = data.Entries.Count(HasConflict);

        writer.WriteLine(
            $"  Devices: {data.Entries.Count}   " +
            $"Exceptions shown: {visible.Count}   " +
            $"Suppressed: {suppressed}"
        );

        writer.WriteLine(
            $"  Noncompliant: {noncompliant}   " +
            $"Unknown/missing: {unknown}   " +
            $"Status conflicts: {conflicts}"
        );

        writer.WriteLine();

        if (visible.Count == 0)
        {
            writer.WriteLine(
                "  No device exceptions in returned inventory."
            );
        }

        foreach (var entry in visible)
        {
            var device = entry.Device;

            writer.WriteLine(
                $"  {Display(device.DeviceName)} " +
                $"[{Display(device.ComplianceState).ToUpperInvariant()}]"
            );

            writer.WriteLine(
                "    Last check-in: " +
                FormatCheckIn(device.LastSyncDateTime, now)
            );

            WriteReasons(writer, entry);

            writer.WriteLine();
        }

        writer.WriteLine(
            $"  SETTING EVIDENCE: " +
            $"Retrieved {data.RetrievedSettingCount}, " +
            $"matched {data.MatchedSettingCount}, " +
            $"unmatched {data.UnmatchedSettings.Count}"
        );

        if (data.UnmatchedSettings.Count > 0)
        {
            var missingIds = data.UnmatchedSettings.Count(
                setting => string.IsNullOrWhiteSpace(
                    setting.DeviceId)
            );

            var absentIds =
                data.UnmatchedSettings.Count - missingIds;

            writer.WriteLine(
                "  WARNING: Unmatched evidence belongs " +
                "to no confirmed inventory device."
            );

            if (missingIds > 0)
            {
                writer.WriteLine(
                    $"    Missing device ID: {missingIds}"
                );
            }

            if (absentIds > 0)
            {
                writer.WriteLine(
                    $"    ID absent from inventory: {absentIds}"
                );
            }
        }
    }

    private static void WriteReasons(
        TextWriter writer,
        ComplianceDeviceListEntry entry)
    {
        var failed = entry.Settings
            .Where(IsFailure)
            .OrderBy(
                setting => setting.SettingName,
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        var unknown = entry.Settings
            .Where(IsUnknown)
            .OrderBy(
                setting => setting.SettingName,
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        var other = entry.Settings
            .Where(setting =>
                !IsFailure(setting)
                && !IsUnknown(setting)
                && !IsCompliant(setting))
            .OrderBy(
                setting => setting.SettingName,
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();

        foreach (var setting in failed)
        {
            writer.WriteLine(
                $"    FAILED:  {Display(setting.SettingName)}"
            );
        }

        foreach (var setting in unknown)
        {
            writer.WriteLine(
                $"    UNKNOWN: {Display(setting.SettingName)}"
            );
        }

        foreach (var setting in other)
        {
            writer.WriteLine(
                $"    OTHER:   {Display(setting.SettingName)} " +
                $"({Display(setting.State)})"
            );
        }

        if (failed.Count == 0)
        {
            writer.WriteLine(
                entry.Settings.Count == 0
                    ? "    REASON: No matching setting evidence."
                    : "    REASON: No failed setting identified."
            );
        }

        if (HasConflict(entry))
        {
            writer.WriteLine(
                "    CONFLICT: Device reports compliant " +
                "but setting evidence reports a failure."
            );
        }
    }

    private static bool ShouldShow(
        ComplianceDeviceListEntry entry)
    {
        return !IsCompliantState(entry.Device.ComplianceState)
            || HasConflict(entry);
    }

    private static bool HasConflict(
        ComplianceDeviceListEntry entry)
    {
        return IsCompliantState(entry.Device.ComplianceState)
            && entry.Settings.Any(IsFailure);
    }

    private static bool IsCompliantState(string? state)
    {
        return string.Equals(
            state?.Trim(),
            "compliant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsFailure(
        ComplianceSettingEvidence setting)
    {
        return string.Equals(
            setting.State?.Trim(),
            "nonCompliant",
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static bool IsUnknown(
        ComplianceSettingEvidence setting)
    {
        return string.IsNullOrWhiteSpace(setting.State)
            || string.Equals(
                setting.State.Trim(),
                "unknown",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsCompliant(
        ComplianceSettingEvidence setting)
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

        return IsCompliantState(state) ? 3 : 2;
    }

    private static string FormatCheckIn(
        DateTimeOffset? value,
        DateTimeOffset now)
    {
        if (!value.HasValue)
        {
            return "not provided";
        }

        var timestamp = FormatDate(value.Value);

        if (value.Value > now)
        {
            return timestamp + " (future timestamp)";
        }

        var age = now - value.Value;

        string elapsed;

        if (age.TotalDays >= 1)
        {
            elapsed =
                $"{(int)Math.Floor(age.TotalDays)}d";
        }
        else if (age.TotalHours >= 1)
        {
            elapsed =
                $"{(int)Math.Floor(age.TotalHours)}h";
        }
        else
        {
            elapsed = "<1h";
        }

        return timestamp + $" ({elapsed} ago)";
    }

    private static string FormatDate(
        DateTimeOffset value)
    {
        return value.UtcDateTime.ToString(
            "yyyy-MM-dd HH:mm 'UTC'",
            CultureInfo.InvariantCulture
        );
    }

    private static string Display(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "not provided"
            : value.Trim();
    }
}
