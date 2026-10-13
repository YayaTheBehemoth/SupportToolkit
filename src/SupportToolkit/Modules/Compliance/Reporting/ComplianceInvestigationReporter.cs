using System.Globalization;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Reporting;

/// <summary>
/// Read-only presentation of a device investigation.
/// Console mode is compact; note-preview mode is a draft only.
/// </summary>
public sealed class ComplianceInvestigationReporter
{
    public string Render(
        ComplianceInvestigationReport report,
        bool notePreview = false)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        if (notePreview)
        {
            WriteNotePreview(writer, report);
        }
        else
        {
            WriteConsoleReport(writer, report);
        }

        return writer.ToString();
    }

    private static void WriteConsoleReport(
        TextWriter writer,
        ComplianceInvestigationReport report)
    {
        var device = report.Investigation.Device;
        var settings = report.Investigation.MatchedSettings;
        var deviceName = Display(device.DeviceName);

        writer.WriteLine("SUPPORTTOOLKIT // COMPLIANCE");
        writer.WriteLine("===========================");
        writer.WriteLine("DEV PROXY - SYNTHETIC DATA");
        writer.WriteLine();
        writer.WriteLine(deviceName);
        writer.WriteLine(new string('-', Math.Min(deviceName.Length, 50)));

        WriteField(writer, "Compliance", Display(device.ComplianceState).ToUpperInvariant());
        WriteField(writer, "Last check-in", FormatCheckIn(report));
        WriteField(writer, "OS", Display(device.OperatingSystem));
        WriteField(writer, "Graph ID", Display(device.Id));

        writer.WriteLine();
        writer.WriteLine($"SETTING EVIDENCE ({settings.Count})");
        writer.WriteLine("----------------------");
        WriteSettings(writer, settings);

        writer.WriteLine();
        writer.WriteLine("ASSESSMENT");
        writer.WriteLine("----------");
        WriteAssessment(writer, report);

        writer.WriteLine();
        writer.WriteLine("DATA LIMITATIONS");
        writer.WriteLine("----------------");
        WriteLimitations(writer, report);

        writer.WriteLine();
        writer.WriteLine("Investigation only. No case action performed.");
    }

    private static void WriteNotePreview(
        TextWriter writer,
        ComplianceInvestigationReport report)
    {
        var device = report.Investigation.Device;

        writer.WriteLine("COMPLIANCE INVESTIGATION - DRAFT INTERNAL NOTE");
        writer.WriteLine("==============================================");
        writer.WriteLine("SYNTHETIC TEST DATA - DO NOT POST TO CUSTOMER TICKETS");
        writer.WriteLine();
        writer.WriteLine($"Investigation time: {FormatDate(report.GeneratedAtUtc)}");
        writer.WriteLine($"Device: {Display(device.DeviceName)}");
        writer.WriteLine($"Device ID: {Display(device.Id)}");
        writer.WriteLine($"Operating system: {Display(device.OperatingSystem)}");
        writer.WriteLine($"Reported compliance: {Display(device.ComplianceState)}");
        writer.WriteLine($"Last check-in: {FormatCheckIn(report)}");
        writer.WriteLine();

        writer.WriteLine("Setting observations:");
        WriteSettings(writer, report.Investigation.MatchedSettings, noteMode: true);
        writer.WriteLine();

        writer.WriteLine("Assessment:");
        WriteAssessment(writer, report, noteMode: true);
        writer.WriteLine();

        writer.WriteLine("Evidence limitations:");
        WriteLimitations(writer, report, noteMode: true);
        writer.WriteLine();

        writer.WriteLine("No remediation, verification or case closure performed.");
        writer.WriteLine("DRAFT ONLY - nothing submitted to Zendesk.");
    }

    private static void WriteSettings(
        TextWriter writer,
        IReadOnlyList<ComplianceSettingEvidence> settings,
        bool noteMode = false)
    {
        if (settings.Count == 0)
        {
            writer.WriteLine(noteMode
                ? "- No matching setting states returned."
                : "  NO MATCHING SETTING STATES");
            writer.WriteLine(noteMode
                ? "  This does not establish compliance."
                : "  Not evidence that policies passed.");
            return;
        }

        foreach (var setting in settings
            .OrderBy(setting => StateOrder(setting.State))
            .ThenBy(setting => setting.SettingName, StringComparer.OrdinalIgnoreCase))
        {
            var state = FormatState(setting.State);
            writer.WriteLine(noteMode
                ? $"- {Display(setting.SettingName)}: {state}"
                : $"  [{state,-12}] {Display(setting.SettingName)}");
        }
    }

    private static void WriteAssessment(
        TextWriter writer,
        ComplianceInvestigationReport report,
        bool noteMode = false)
    {
        var failed = report.Investigation.MatchedSettings.Count(setting =>
            string.Equals(setting.State, "nonCompliant", StringComparison.OrdinalIgnoreCase));

        var prefix = noteMode ? "- " : "  ";

        if (failed > 0)
        {
            writer.WriteLine(prefix + $"{failed} setting " +
                (failed == 1 ? "is" : "are") + " reported noncompliant.");
            writer.WriteLine(prefix + "These are candidate findings, not verified causes.");
        }
        else
        {
            writer.WriteLine(prefix + "No matched settings reported noncompliant.");
            writer.WriteLine(prefix + "This does not establish overall compliance.");
        }

        if (report.TimeSinceLastCheckIn is TimeSpan age)
        {
            writer.WriteLine(prefix + $"Last check-in was {FormatAge(age)} ago.");
            writer.WriteLine(prefix + "No staleness threshold has been applied.");
        }
        else
        {
            writer.WriteLine(prefix + "Check-in age cannot be determined.");
        }
    }

    private static void WriteLimitations(
        TextWriter writer,
        ComplianceInvestigationReport report,
        bool noteMode = false)
    {
        var device = report.Investigation.Device;
        var settings = report.Investigation.MatchedSettings;
        var prefix = noteMode ? "- " : "  - ";

        var unknownCount = settings.Count(setting =>
            string.IsNullOrWhiteSpace(setting.State) ||
            string.Equals(setting.State, "unknown", StringComparison.OrdinalIgnoreCase));

        if (unknownCount > 0)
        {
            writer.WriteLine(prefix + $"Unknown/missing setting states: {unknownCount}.");
        }

        if (!device.LastSyncDateTime.HasValue)
        {
            writer.WriteLine(prefix + "Last check-in timestamp not provided.");
        }
        else if (device.LastSyncDateTime.Value > report.GeneratedAtUtc)
        {
            writer.WriteLine(prefix + "Check-in timestamp is in the future.");
        }

        if (settings.Count == 0)
        {
            writer.WriteLine(prefix + "No matching setting evidence; coverage unknown.");
        }

        writer.WriteLine(prefix + "Setting/device ID correlation unverified in Intune.");
        writer.WriteLine(prefix + "Tenant context and policy coverage unverified.");
        writer.WriteLine(prefix + "Synthetic responses; live Graph behavior unverified.");
    }

    private static void WriteField(TextWriter writer, string label, string value)
    {
        writer.WriteLine($"  {label,-15} {value}");
    }

    private static int StateOrder(string? state)
    {
        if (string.Equals(state, "nonCompliant", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(state) ||
            string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return string.Equals(state, "compliant", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
    }

    private static string FormatState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? "NOT PROVIDED" : state.Trim().ToUpperInvariant();

    private static string FormatCheckIn(ComplianceInvestigationReport report)
    {
        var timestamp = report.Investigation.Device.LastSyncDateTime;

        if (!timestamp.HasValue)
        {
            return "not provided";
        }

        var date = FormatDate(timestamp);

        return report.TimeSinceLastCheckIn is TimeSpan age
            ? date + $" ({FormatAge(age)} ago)"
            : date + " (age unavailable)";
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : "not provided";

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1)
        {
            var days = (int)Math.Floor(age.TotalDays);
            return days == 1 ? "1 day" : $"{days} days";
        }

        if (age.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(age.TotalHours);
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }

        return "less than 1 hour";
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not provided" : value.Trim();
}
