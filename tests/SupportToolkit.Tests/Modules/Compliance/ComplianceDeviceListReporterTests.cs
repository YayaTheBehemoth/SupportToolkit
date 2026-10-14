
using SupportToolkit.Modules.Compliance.Reporting;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceDeviceListReporterTests
{
    private static readonly DateTimeOffset ReportTime =
        new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Render_ShowsFailedSettingForNoncompliantDevice()
    {
        var output = Render(
            Entry(
                "STALE-01",
                "noncompliant",
                ReportTime.AddDays(-73),
                Setting("Device encryption required", "nonCompliant"),
                Setting("Minimum OS version", "unknown")
            )
        );

        Assert.Contains(
            "FAILED:   Device encryption required",
            output
        );

        Assert.Contains(
            "UNKNOWN:  Minimum OS version",
            output
        );

        Assert.Contains("73d", output);
        Assert.Contains("NONCOMPLIANT: 1", output);
    }

    [Fact]
    public void Render_SuppressesReportedCompliantDevices()
    {
        var output = Render(
            Entry(
                "STALE-01",
                "noncompliant",
                null,
                Setting("Encryption", "nonCompliant")
            ),
            Entry(
                "HEALTHY-02",
                "compliant",
                ReportTime,
                Setting("Encryption", "compliant")
            ),
            Entry("UNKNOWN-03", "unknown")
        );

        Assert.Contains("DEVICES: 3", output);
        Assert.Contains("SHOWN: 2", output);
        Assert.Contains("SUPPRESSED: 1", output);

        Assert.Contains("STALE-01", output);
        Assert.Contains("UNKNOWN-03", output);
        Assert.DoesNotContain("HEALTHY-02", output);

        Assert.Contains(
            "All 3 device records accounted for",
            output
        );
    }

    [Fact]
    public void Render_UnknownDeviceDoesNotInventReason()
    {
        var output = Render(
            Entry("UNKNOWN-03", "unknown")
        );

        Assert.Contains(
            "REASON:   No matching setting evidence",
            output
        );

        Assert.DoesNotContain("FAILED:", output);
    }

    [Fact]
    public void Render_NoncompliantWithoutFailureKeepsReasonUnknown()
    {
        var output = Render(
            Entry(
                "STALE-01",
                "noncompliant",
                null,
                Setting("Encryption", "compliant")
            )
        );

        Assert.Contains(
            "REASON:   No failed setting identified",
            output
        );

        Assert.DoesNotContain("FAILED:", output);
    }

    [Fact]
    public void Render_ContradictoryEvidencePreventsSuppression()
    {
        var output = Render(
            Entry(
                "HEALTHY-02",
                "compliant",
                ReportTime,
                Setting("Encryption", "nonCompliant")
            )
        );

        Assert.Contains("SHOWN: 1", output);
        Assert.Contains("SUPPRESSED: 0", output);

        Assert.Contains(
            "STATUS/SETTING CONFLICTS: 1",
            output
        );

        Assert.Contains("HEALTHY-02", output);
        Assert.Contains("CONFLICT:", output);
    }

    [Fact]
    public void Render_OrdersNoncompliantBeforeUnknown()
    {
        var output = Render(
            Entry("UNKNOWN-03", "unknown"),
            Entry("STALE-01", "noncompliant")
        );

        var staleIndex = output.IndexOf(
            "STALE-01",
            StringComparison.Ordinal
        );

        var unknownIndex = output.IndexOf(
            "UNKNOWN-03",
            StringComparison.Ordinal
        );

        Assert.True(staleIndex >= 0);
        Assert.True(unknownIndex > staleIndex);
    }

    [Fact]
    public void Render_UnexpectedSettingStateRemainsVisible()
    {
        var output = Render(
            Entry(
                "DEVICE-01",
                "noncompliant",
                null,
                Setting("Security requirement", "error")
            )
        );

        Assert.Contains(
            "OTHER:    Security requirement (error)",
            output
        );

        Assert.Contains(
            "REASON:   No failed setting identified",
            output
        );
    }

    [Fact]
    public void Render_EmptyListDoesNotClaimEverythingIsHealthy()
    {
        var output = Render();

        Assert.Contains("DEVICES: 0", output);

        Assert.Contains(
            "No managed devices returned",
            output
        );

        Assert.Contains("SUPPRESSED: 0", output);
    }

    [Fact]
    public void Render_AllMatchedSettingsAreAccountedFor()
    {
        var output = Render(
            Entry(
                "STALE-01",
                "noncompliant",
                null,
                Setting("Encryption", "nonCompliant"),
                Setting("OS version", "unknown")
            )
        );

        Assert.Contains(
            "Retrieved: 2   Matched: 2   Unmatched: 0",
            output
        );

        Assert.DoesNotContain(
            "WARNING: Some setting states",
            output
        );
    }

    [Fact]
    public void Render_UnmatchedStatesProduceCoverageWarning()
    {
        var result = new ComplianceDeviceListResult(
        [
            Entry(
                "STALE-01",
                "noncompliant",
                null,
                Setting("Encryption", "nonCompliant")
            )
        ])
        {
            UnmatchedSettings =
            [
                new ComplianceSettingEvidence(
                    "summary-002",
                    "OS version",
                    "device-not-in-inventory",
                    "OTHER-DEVICE",
                    "nonCompliant"
                ),
                new ComplianceSettingEvidence(
                    "summary-003",
                    "Unknown association",
                    null,
                    null,
                    "unknown"
                )
            ]
        };

        var output = new ComplianceDeviceListReporter()
            .Render(
                result,
                new FixedTimeProvider(ReportTime)
            );

        Assert.Contains(
            "Retrieved: 3   Matched: 1   Unmatched: 2",
            output
        );

        Assert.Contains(
            "WARNING: Some setting states",
            output
        );

        Assert.Contains(
            "Missing device ID: 1",
            output
        );

        Assert.Contains(
            "Device ID absent from inventory: 1",
            output
        );

        Assert.Contains(
            "Unmatched states were not assigned",
            output
        );

        Assert.Contains(
            "does not establish device removal or recovery",
            output
        );

        // Never invent an inventory device from an orphaned
        // setting-state record.
        Assert.DoesNotContain(
            "OTHER-DEVICE",
            output
        );
    }

    [Fact]
    public void Render_EmptyInventoryCanStillHaveUnmatchedEvidence()
    {
        var result = new ComplianceDeviceListResult([])
        {
            UnmatchedSettings =
            [
                new ComplianceSettingEvidence(
                    "summary-001",
                    "Encryption",
                    "orphan-device",
                    null,
                    "nonCompliant"
                )
            ]
        };

        var output = new ComplianceDeviceListReporter()
            .Render(
                result,
                new FixedTimeProvider(ReportTime)
            );

        Assert.Contains("DEVICES: 0", output);
        Assert.Contains("No managed devices returned", output);

        Assert.Contains(
            "Retrieved: 1   Matched: 0   Unmatched: 1",
            output
        );

        Assert.Contains(
            "Device ID absent from inventory: 1",
            output
        );
    }

    private static string Render(
        params ComplianceDeviceListEntry[] entries)
    {
        return new ComplianceDeviceListReporter().Render(
            new ComplianceDeviceListResult(entries),
            new FixedTimeProvider(ReportTime)
        );
    }

    private static ComplianceDeviceListEntry Entry(
        string name,
        string state,
        DateTimeOffset? checkIn = null,
        params ComplianceSettingEvidence[] settings)
    {
        return new ComplianceDeviceListEntry(
            new ManagedDevice
            {
                Id = name,
                DeviceName = name,
                ComplianceState = state,
                LastSyncDateTime = checkIn
            },
            settings
        );
    }

    private static ComplianceSettingEvidence Setting(
        string name,
        string? state)
    {
        return new ComplianceSettingEvidence(
            "summary-001",
            name,
            "synthetic-device",
            "synthetic-name",
            state
        );
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
