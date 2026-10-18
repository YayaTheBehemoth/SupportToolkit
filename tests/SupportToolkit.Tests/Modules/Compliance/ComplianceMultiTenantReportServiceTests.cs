
using System.Net;
using SupportToolkit.Modules.Compliance.Fixtures;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceMultiTenantReportServiceTests
{
    [Fact]
    public async Task CollectAsync_KeepsReusedDeviceIdsTenantScoped()
    {
        var alpha = Source(
            "alpha",
            [Device("shared-id", "SAME-NAME")],
            [Setting("shared-id", "Alpha encryption")]
        );

        var beta = Source(
            "beta",
            [Device("shared-id", "SAME-NAME")],
            [Setting("shared-id", "Beta OS requirement")]
        );

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync([alpha, beta]);

        Assert.True(result.AllSelectedCollectionsSucceeded);
        Assert.Equal(2, result.SuccessfulTenants);

        var alphaSetting = Assert.Single(
            result.Tenants[0].Data!.Entries[0].Settings
        );

        var betaSetting = Assert.Single(
            result.Tenants[1].Data!.Entries[0].Settings
        );

        Assert.Equal(
            "Alpha encryption",
            alphaSetting.SettingName
        );

        Assert.Equal(
            "Beta OS requirement",
            betaSetting.SettingName
        );

        Assert.DoesNotContain(
            result.Tenants[0].Data!.Entries[0].Settings,
            item => item.SettingName == "Beta OS requirement"
        );
    }

    [Fact]
    public async Task CollectAsync_FailedTenantDoesNotDiscardSuccesses()
    {
        var alpha = Source(
            "alpha",
            [Device("alpha-001", "ALPHA-DEVICE")],
            [Setting("alpha-001", "Encryption")]
        );

        var beta = Source(
            "beta",
            [Device("beta-001", "BETA-DEVICE")],
            [],
            settingFailure: new HttpRequestException(
                "Do not expose this detail",
                null,
                HttpStatusCode.Forbidden
            )
        );

        var gamma = Source(
            "gamma",
            [Device("gamma-001", "GAMMA-DEVICE")],
            []
        );

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync([alpha, beta, gamma]);

        Assert.Equal(3, result.Tenants.Count);
        Assert.Equal(2, result.SuccessfulTenants);
        Assert.Equal(1, result.FailedTenants);

        Assert.False(
            result.AllSelectedCollectionsSucceeded
        );

        Assert.NotNull(result.Tenants[0].Data);
        Assert.Null(result.Tenants[1].Data);
        Assert.NotNull(result.Tenants[2].Data);

        Assert.Equal(
            "HTTP 403",
            result.Tenants[1].FailureReason
        );

        Assert.DoesNotContain(
            "Do not expose",
            result.Tenants[1].FailureReason!
        );
    }

    [Fact]
    public async Task CollectAsync_RejectsDuplicateTenantKeys()
    {
        var first = Source("alpha", [], []);
        var second = Source("ALPHA", [], []);

        var service = new ComplianceMultiTenantReportService();

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.CollectAsync([first, second])
        );
    }

    [Fact]
    public async Task CollectAsync_RejectsEmptyScope()
    {
        var service = new ComplianceMultiTenantReportService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CollectAsync([])
        );
    }

    [Fact]
    public async Task CollectAsync_UnmatchedEvidenceStaysWithSourceTenant()
    {
        var alpha = Source(
            "alpha",
            [Device("alpha-001", "DEVICE-01")],
            [Setting("missing-alpha", "Unmatched Alpha setting")]
        );

        var beta = Source(
            "beta",
            [Device("beta-001", "DEVICE-02")],
            [Setting("beta-001", "Matched Beta setting")]
        );

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync([alpha, beta]);

        Assert.Single(
            result.Tenants[0].Data!.UnmatchedSettings
        );

        Assert.Empty(
            result.Tenants[1].Data!.UnmatchedSettings
        );

        Assert.Empty(
            result.Tenants[0].Data!.Entries[0].Settings
        );

        Assert.Single(
            result.Tenants[1].Data!.Entries[0].Settings
        );
    }

    [Fact]
    public async Task CollectAsync_DuplicateIdFailureIsTenantLocal()
    {
        var bad = Source(
            "alpha",
            [
                Device("duplicate", "DEVICE-01"),
                Device("DUPLICATE", "DEVICE-02")
            ],
            []
        );

        var good = Source(
            "beta",
            [Device("beta-001", "DEVICE-03")],
            []
        );

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync([bad, good]);

        Assert.Null(result.Tenants[0].Data);

        Assert.Equal(
            "Invalid or inconsistent collection data",
            result.Tenants[0].FailureReason
        );

        Assert.NotNull(result.Tenants[1].Data);
        Assert.Equal(1, result.FailedTenants);
    }

    [Fact]
    public async Task CollectAsync_EmptyInventoryIsObservedNotFailed()
    {
        var empty = Source("alpha", [], []);

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync([empty]);

        Assert.True(result.AllSelectedCollectionsSucceeded);

        Assert.Empty(
            result.Tenants[0].Data!.Entries
        );

        Assert.Equal(0, result.FailedTenants);
    }

    [Fact]
    public async Task CollectAsync_PropagatesOverallCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var service = new ComplianceMultiTenantReportService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CollectAsync(
                [Source("alpha", [], [])],
                cancellation.Token
            )
        );
    }

    [Fact]
    public async Task FixtureFactory_ProducesIndependentTenantReports()
    {
        var sources = ComplianceSyntheticTenantFixtures.Create(
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026, 10, 17, 12, 0, 0,
                    TimeSpan.Zero
                )
            )
        );

        var result = await new ComplianceMultiTenantReportService()
            .CollectAsync(sources);

        Assert.Equal(3, result.SuccessfulTenants);
        Assert.Equal(0, result.FailedTenants);

        Assert.Equal(
            8,
            result.Tenants.Sum(
                tenant => tenant.Data!.Entries.Count
            )
        );

        Assert.Equal(
            1,
            result.Tenants.Sum(
                tenant => tenant.Data!.UnmatchedSettings.Count
            )
        );

        var alpha = result.Tenants[0].Data!;
        var beta = result.Tenants[1].Data!;

        // Both tenants contain the same synthetic device ID.
        Assert.Equal(
            alpha.Entries[0].Device.Id,
            beta.Entries[0].Device.Id
        );

        Assert.Equal(
            "Device encryption required",
            alpha.Entries[0].Settings[0].SettingName
        );

        Assert.Equal(
            "Operating system requirement",
            beta.Entries[0].Settings[0].SettingName
        );
    }

    private static ComplianceTenantSource Source(
        string key,
        IReadOnlyList<ManagedDevice> devices,
        IReadOnlyList<ComplianceSettingEvidence> evidence,
        Exception? settingFailure = null)
    {
        return new ComplianceTenantSource(
            new ComplianceTenantScope(key, "Tenant " + key),
            new StubDevices(devices),
            new StubSettings(evidence, settingFailure)
        );
    }

    private static ManagedDevice Device(
        string id,
        string name)
    {
        return new ManagedDevice
        {
            Id = id,
            DeviceName = name,
            ComplianceState = "noncompliant"
        };
    }

    private static ComplianceSettingEvidence Setting(
        string deviceId,
        string name)
    {
        return new ComplianceSettingEvidence(
            "summary-001",
            name,
            deviceId,
            null,
            "nonCompliant"
        );
    }

    private sealed class StubDevices(
        IReadOnlyList<ManagedDevice> devices)
        : IGraphManagedDeviceProvider
    {
        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(devices);
        }
    }

    private sealed class StubSettings(
        IReadOnlyList<ComplianceSettingEvidence> evidence,
        Exception? failure)
        : IGraphComplianceSettingProvider
    {
        public Task<IReadOnlyList<ComplianceSettingEvidence>>
            GetSettingEvidenceAsync(
                CancellationToken cancellationToken = default)
        {
            if (failure is not null)
            {
                return Task.FromException<
                    IReadOnlyList<ComplianceSettingEvidence>
                >(failure);
            }

            return Task.FromResult(evidence);
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset now)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
