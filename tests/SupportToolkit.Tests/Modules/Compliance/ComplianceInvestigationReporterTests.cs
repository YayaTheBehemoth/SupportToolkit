using SupportToolkit.Modules.Compliance.Reporting;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceInvestigationReporterTests
{
    private static readonly DateTimeOffset ReportTime =
        new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Render_ShowsCompactDeviceSummary()
    {
        var report = CreateReport(ReportTime.AddDays(-73), "noncompliant");
        var output = new ComplianceInvestigationReporter().Render(report);

        Assert.Contains("SUPPORTTOOLKIT // COMPLIANCE", output);
        Assert.Contains("DEV PROXY - SYNTHETIC DATA", output);
        Assert.Contains("NONCOMPLIANT", output);
        Assert.Contains("73 days ago", output);
        Assert.Contains("SETTING EVIDENCE (0)", output);
        Assert.Contains("ASSESSMENT", output);
        Assert.Contains("DATA LIMITATIONS", output);
        Assert.Contains("No staleness threshold has been applied", output);
    }

    [Fact]
    public void Render_PrioritizesFailedAndUnknownSettings()
    {
        var report = CreateReport(
            ReportTime.AddDays(-5),
            "noncompliant",
            Setting("Healthy setting", "compliant"),
            Setting("Unknown setting", "unknown"),
            Setting("Encryption failure", "nonCompliant"));

        var output = new ComplianceInvestigationReporter().Render(report);

        var failed = output.IndexOf("[NONCOMPLIANT]", StringComparison.Ordinal);
        var unknown = output.IndexOf("[UNKNOWN", StringComparison.Ordinal);
        var compliant = output.IndexOf("[COMPLIANT", StringComparison.Ordinal);

        Assert.True(failed >= 0);
        Assert.True(unknown > failed);
        Assert.True(compliant > unknown);
        Assert.Contains("1 setting is reported noncompliant", output);
        Assert.Contains("Unknown/missing setting states: 1", output);
        Assert.Contains("candidate findings, not verified causes", output);
    }

    [Fact]
    public void Render_CompliantSettingDoesNotProveOverallCompliance()
    {
        var report = CreateReport(
            ReportTime.AddDays(-1), "compliant",
            Setting("Device encryption", "compliant"));

        var output = new ComplianceInvestigationReporter().Render(report);

        Assert.Contains("No matched settings reported noncompliant", output);
        Assert.Contains("This does not establish overall compliance", output);
    }

    [Fact]
    public void Render_NoMatchingSettingsRemainsUncertain()
    {
        var output = new ComplianceInvestigationReporter()
            .Render(CreateReport(ReportTime.AddDays(-2), "unknown"));

        Assert.Contains("NO MATCHING SETTING STATES", output);
        Assert.Contains("Not evidence that policies passed", output);
        Assert.Contains("No matching setting evidence; coverage unknown", output);
    }

    [Fact]
    public void Render_MissingCheckInDoesNotInventAge()
    {
        var output = new ComplianceInvestigationReporter()
            .Render(CreateReport(null, "unknown"));

        Assert.Contains("Last check-in", output);
        Assert.Contains("Check-in age cannot be determined", output);
        Assert.Contains("Last check-in timestamp not provided", output);
        Assert.DoesNotContain("0 days ago", output);
    }

    [Fact]
    public void Render_FutureCheckInIsFlagged()
    {
        var report = CreateReport(ReportTime.AddDays(1), "noncompliant");
        Assert.Null(report.TimeSinceLastCheckIn);

        var output = new ComplianceInvestigationReporter().Render(report);

        Assert.Contains("age unavailable", output);
        Assert.Contains("Check-in timestamp is in the future", output);
        Assert.DoesNotContain("-1 days ago", output);
    }

    [Fact]
    public void Render_NotePreviewClearlyIndicatesDraft()
    {
        var report = CreateReport(
            ReportTime.AddDays(-3), "noncompliant",
            Setting("Encryption", "nonCompliant"));

        var output = new ComplianceInvestigationReporter()
            .Render(report, notePreview: true);

        Assert.Contains("DRAFT INTERNAL NOTE", output);
        Assert.Contains("SYNTHETIC TEST DATA", output);
        Assert.Contains("Setting observations:", output);
        Assert.Contains("Assessment:", output);
        Assert.Contains("Evidence limitations:", output);
        Assert.Contains("nothing submitted to Zendesk", output);
        Assert.DoesNotContain("PASS:", output);
    }

    [Fact]
    public void Render_DoesNotMakeCaseLifecycleDecision()
    {
        var report = CreateReport(
            ReportTime.AddDays(-10), "noncompliant",
            Setting("Encryption", "nonCompliant"));

        var output = new ComplianceInvestigationReporter().Render(report);

        Assert.Contains("No case action performed", output);
        Assert.Contains("correlation unverified", output);
    }

    private static ComplianceSettingEvidence Setting(string name, string? state) =>
        new("summary-001", name, "device-001", "STK-SYNTHETIC-STALE-01", state);

    private static ComplianceInvestigationReport CreateReport(
        DateTimeOffset? lastSync,
        string? complianceState,
        params ComplianceSettingEvidence[] settings)
    {
        var device = new ManagedDevice
        {
            Id = "device-001",
            DeviceName = "STK-SYNTHETIC-STALE-01",
            OperatingSystem = "Windows",
            ComplianceState = complianceState,
            LastSyncDateTime = lastSync
        };

        var investigation = new ComplianceEvidenceInvestigation(device, settings);

        return new ComplianceInvestigationReportFactory(
            new FixedTimeProvider(ReportTime)).Create(investigation);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
