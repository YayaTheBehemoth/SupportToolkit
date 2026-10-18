
using SupportToolkit.Modules.Compliance.Fixtures;
using SupportToolkit.Modules.Compliance.Reporting;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceMultiTenantReporterTests
{
    private static readonly DateTimeOffset ReportTime =
        new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Render_GroupsExceptionsUnderCorrectTenant()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry(
                    "ALPHA-DEVICE",
                    "noncompliant",
                    Setting("Alpha encryption", "nonCompliant")
                )
            ),
            Collected(
                "beta",
                Entry(
                    "BETA-DEVICE",
                    "noncompliant",
                    Setting("Beta OS requirement", "nonCompliant")
                )
            )
        );

        var output = Render(report);

        var alphaIndex = output.IndexOf(
            "TENANT: Tenant alpha",
            StringComparison.Ordinal
        );

        var betaIndex = output.IndexOf(
            "TENANT: Tenant beta",
            StringComparison.Ordinal
        );

        var alphaFinding = output.IndexOf(
            "Alpha encryption",
            StringComparison.Ordinal
        );

        var betaFinding = output.IndexOf(
            "Beta OS requirement",
            StringComparison.Ordinal
        );

        Assert.True(alphaIndex >= 0);
        Assert.True(alphaFinding > alphaIndex);
        Assert.True(betaIndex > alphaFinding);
        Assert.True(betaFinding > betaIndex);
    }

    [Fact]
    public void Render_SuppressesReportedCompliantDeviceDetails()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry(
                    "FAILED-DEVICE",
                    "noncompliant",
                    Setting("Encryption", "nonCompliant")
                ),
                Entry("HEALTHY-DEVICE", "compliant")
            )
        );

        var output = Render(report);

        Assert.Contains("Observed devices: 2", output);
        Assert.Contains("Shown: 1", output);
        Assert.Contains("Suppressed: 1", output);
        Assert.Contains("FAILED-DEVICE", output);

        Assert.DoesNotContain("HEALTHY-DEVICE", output);
    }

    [Fact]
    public void Render_PartialReportDoesNotTreatFailureAsZero()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry(
                    "ALPHA-DEVICE",
                    "noncompliant",
                    Setting("Encryption", "nonCompliant")
                )
            ),
            new ComplianceTenantCollection(
                new ComplianceTenantScope(
                    "beta",
                    "Tenant beta"
                ),
                null,
                "HTTP 403"
            ),
            Collected(
                "gamma",
                Entry("GAMMA-DEVICE", "compliant")
            )
        );

        var output = Render(report);

        Assert.Contains(
            "COLLECTION: PARTIAL - TENANT FAILURES",
            output
        );

        Assert.Contains(
            "2 collected / 1 failed",
            output
        );

        Assert.Contains(
            "Observed devices: 2",
            output
        );

        Assert.Contains(
            "COLLECTION FAILED: HTTP 403",
            output
        );

        Assert.Contains(
            "UNAVAILABLE, not zero",
            output
        );

        Assert.Contains("ALPHA-DEVICE", output);
        Assert.Contains("TENANT: Tenant gamma", output);
        Assert.DoesNotContain("GAMMA-DEVICE", output);
    }

    [Fact]
    public void Render_HealthyOnlyTenantRemainsInSummary()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry("HEALTHY-01", "compliant"),
                Entry("HEALTHY-02", "compliant")
            )
        );

        var output = Render(report);

        Assert.Contains(
            "TENANT: Tenant alpha",
            output
        );

        Assert.Contains(
            "Devices: 2",
            output
        );

        Assert.Contains(
            "Exceptions shown: 0",
            output
        );

        Assert.Contains(
            "No device exceptions in returned inventory",
            output
        );

        Assert.DoesNotContain(
            "HEALTHY-01",
            output
        );
    }

    [Fact]
    public void Render_CompliantDeviceWithFailedSettingRemainsVisible()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry(
                    "CONFLICT-DEVICE",
                    "compliant",
                    Setting("Encryption", "nonCompliant")
                )
            )
        );

        var output = Render(report);

        Assert.Contains("CONFLICT-DEVICE", output);
        Assert.Contains("Status conflicts: 1", output);
        Assert.Contains("FAILED:  Encryption", output);
        Assert.Contains("CONFLICT:", output);
        Assert.Contains("Shown: 1", output);
        Assert.Contains("Suppressed: 0", output);
    }

    [Fact]
    public void Render_UnknownDeviceWithoutEvidenceRemainsVisible()
    {
        var report = Report(
            Collected(
                "alpha",
                Entry("UNKNOWN-DEVICE", "unknown")
            )
        );

        var output = Render(report);

        Assert.Contains("UNKNOWN-DEVICE", output);
        Assert.Contains("Last check-in: not provided", output);
        Assert.Contains(
            "REASON: No matching setting evidence",
            output
        );
    }

    [Fact]
    public void Render_UnmatchedEvidenceIsTenantScoped()
    {
        var alphaData = new ComplianceDeviceListResult(
        [
            Entry("ALPHA-DEVICE", "noncompliant")
        ])
        {
            UnmatchedSettings =
            [
                new ComplianceSettingEvidence(
                    "summary-001",
                    "Unmatched setting",
                    "missing-device",
                    null,
                    "unknown"
                )
            ]
        };

        var report = Report(
            new ComplianceTenantCollection(
                new ComplianceTenantScope(
                    "alpha",
                    "Tenant alpha"
                ),
                alphaData,
                null
            ),
            Collected(
                "beta",
                Entry("BETA-DEVICE", "noncompliant")
            )
        );

        var output = Render(report);

        var alphaIndex = output.IndexOf(
            "TENANT: Tenant alpha",
            StringComparison.Ordinal
        );

        var betaIndex = output.IndexOf(
            "TENANT: Tenant beta",
            StringComparison.Ordinal
        );

        var warningIndex = output.IndexOf(
            "WARNING: Unmatched evidence",
            StringComparison.Ordinal
        );

        Assert.True(warningIndex > alphaIndex);
        Assert.True(warningIndex < betaIndex);
        Assert.Contains(
            "ID absent from inventory: 1",
            output
        );
    }

    [Fact]
    public void Render_EmptyInventoryIsNotCalledHealthy()
    {
        var report = Report(
            Collected("alpha")
        );

        var output = Render(report);

        Assert.Contains("Devices: 0", output);
        Assert.Contains(
            "No device exceptions in returned inventory",
            output
        );

        Assert.Contains(
            "Full Intune policy and device coverage is unverified",
            output
        );
    }

    [Fact]
    public async Task Render_SyntheticFixturesGiveExpectedSummary()
    {
        var clock = new FixedTimeProvider(ReportTime);

        var sources = ComplianceSyntheticTenantFixtures.Create(
            clock
        );

        var report = await new ComplianceMultiTenantReportService()
            .CollectAsync(sources);

        var output = new ComplianceMultiTenantReporter()
            .Render(report, clock);

        Assert.Contains(
            "3 selected / 3 collected / 0 failed",
            output
        );

        Assert.Contains(
            "Observed devices: 8   Shown: 4   Suppressed: 4",
            output
        );

        Assert.Contains(
            "Unmatched setting observations: 1",
            output
        );

        Assert.Contains("Tenant Alpha", output);
        Assert.Contains("Tenant Beta", output);
        Assert.Contains("Tenant Gamma", output);

        Assert.Contains(
            "74d ago",
            output
        );

        Assert.Contains(
            "Operating system requirement",
            output
        );

        Assert.Contains(
            "CONFLICT:",
            output
        );

        Assert.DoesNotContain(
            "COLLECTION: PARTIAL",
            output
        );
    }

    private static string Render(
        ComplianceMultiTenantReport report)
    {
        return new ComplianceMultiTenantReporter()
            .Render(
                report,
                new FixedTimeProvider(ReportTime)
            );
    }

    private static ComplianceMultiTenantReport Report(
        params ComplianceTenantCollection[] tenants)
    {
        return new ComplianceMultiTenantReport(tenants);
    }

    private static ComplianceTenantCollection Collected(
        string key,
        params ComplianceDeviceListEntry[] entries)
    {
        return new ComplianceTenantCollection(
            new ComplianceTenantScope(key, "Tenant " + key),
            new ComplianceDeviceListResult(entries),
            null
        );
    }

    private static ComplianceDeviceListEntry Entry(
        string name,
        string state,
        params ComplianceSettingEvidence[] settings)
    {
        return new ComplianceDeviceListEntry(
            new ManagedDevice
            {
                Id = name,
                DeviceName = name,
                ComplianceState = state,
                LastSyncDateTime = null
            },
            settings
        );
    }

    private static ComplianceSettingEvidence Setting(
        string name,
        string state)
    {
        return new ComplianceSettingEvidence(
            "summary-001",
            name,
            "synthetic-device",
            null,
            state
        );
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
