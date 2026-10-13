
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceDeviceListServiceTests
{
    [Fact]
    public async Task GetAsync_RetrievesEachSourceOnceAndMatchesDeviceId()
    {
        var devices = new StubDeviceProvider();
        var settings = new StubSettingProvider();

        var service = new ComplianceDeviceListService(
            devices,
            settings
        );

        var result = await service.GetAsync();

        Assert.Equal(1, devices.CallCount);
        Assert.Equal(1, settings.CallCount);

        Assert.Equal(2, result.Entries.Count);

        var first = result.Entries[0];
        var second = result.Entries[1];

        var matched = Assert.Single(first.Settings);

        Assert.Equal("Encryption", matched.SettingName);
        Assert.Equal("nonCompliant", matched.State);

        Assert.Empty(second.Settings);
    }

    [Fact]
    public async Task GetAsync_DoesNotTurnMissingEvidenceIntoCompliance()
    {
        var service = new ComplianceDeviceListService(
            new StubDeviceProvider(),
            new StubSettingProvider()
        );

        var result = await service.GetAsync();

        var unknown = result.Entries[1];

        Assert.Equal(
            "unknown",
            unknown.Device.ComplianceState
        );

        Assert.Empty(unknown.Settings);
    }

    private sealed class StubDeviceProvider
        : IGraphManagedDeviceProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            IReadOnlyList<ManagedDevice> devices =
            [
                new ManagedDevice
                {
                    Id = "device-001",
                    DeviceName = "STALE-01",
                    ComplianceState = "noncompliant"
                },
                new ManagedDevice
                {
                    Id = "device-002",
                    DeviceName = "UNKNOWN-02",
                    ComplianceState = "unknown"
                }
            ];

            return Task.FromResult(devices);
        }
    }

    private sealed class StubSettingProvider
        : IGraphComplianceSettingProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ComplianceSettingEvidence>>
            GetSettingEvidenceAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            IReadOnlyList<ComplianceSettingEvidence> evidence =
            [
                new ComplianceSettingEvidence(
                    "summary-001",
                    "Encryption",
                    "DEVICE-001",
                    "STALE-01",
                    "nonCompliant"
                )
            ];

            return Task.FromResult(evidence);
        }
    }
}
