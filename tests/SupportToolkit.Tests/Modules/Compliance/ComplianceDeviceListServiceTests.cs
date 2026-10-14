
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

        var matched = Assert.Single(
            result.Entries[0].Settings
        );

        Assert.Equal("Encryption", matched.SettingName);
        Assert.Equal("nonCompliant", matched.State);

        Assert.Empty(result.Entries[1].Settings);
        Assert.Empty(result.UnmatchedSettings);

        Assert.Equal(1, result.MatchedSettingCount);
        Assert.Equal(1, result.RetrievedSettingCount);
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

    [Fact]
    public async Task GetAsync_RejectsDuplicateDeviceIds()
    {
        var devices = new StubDeviceProvider
        {
            Devices =
            [
                Device("device-001", "FIRST"),
                Device("DEVICE-001", "SECOND")
            ]
        };

        var settings = new StubSettingProvider();

        var service = new ComplianceDeviceListService(
            devices,
            settings
        );

        var exception = await Assert.ThrowsAsync<
            InvalidDataException
        >(
            () => service.GetAsync()
        );

        Assert.Contains(
            "Duplicate managed-device ID",
            exception.Message
        );

        // Invalid inventory stops before setting retrieval.
        Assert.Equal(1, devices.CallCount);
        Assert.Equal(0, settings.CallCount);
    }

    [Fact]
    public async Task GetAsync_DuplicateNamesWithDistinctIdsRemainSeparate()
    {
        var devices = new StubDeviceProvider
        {
            Devices =
            [
                Device("device-001", "SAME-NAME"),
                Device("device-002", "SAME-NAME")
            ]
        };

        var service = new ComplianceDeviceListService(
            devices,
            new StubSettingProvider
            {
                Evidence = []
            }
        );

        var result = await service.GetAsync();

        Assert.Equal(2, result.Entries.Count);
    }

    [Fact]
    public async Task GetAsync_PreservesUnmatchedSettingEvidence()
    {
        var settings = new StubSettingProvider
        {
            Evidence =
            [
                Setting("device-001", "Encryption"),
                Setting("missing-device", "OS requirement"),
                Setting(null, "Unknown association")
            ]
        };

        var service = new ComplianceDeviceListService(
            new StubDeviceProvider(),
            settings
        );

        var result = await service.GetAsync();

        Assert.Single(result.Entries[0].Settings);
        Assert.Empty(result.Entries[1].Settings);

        Assert.Equal(2, result.UnmatchedSettings.Count);

        Assert.Contains(
            result.UnmatchedSettings,
            item => item.DeviceId == "missing-device"
        );

        Assert.Contains(
            result.UnmatchedSettings,
            item => item.DeviceId is null
        );

        Assert.Equal(1, result.MatchedSettingCount);
        Assert.Equal(3, result.RetrievedSettingCount);
    }

    [Fact]
    public async Task GetAsync_RejectsDeviceWithMissingId()
    {
        var devices = new StubDeviceProvider
        {
            Devices =
            [
                Device(null, "UNIDENTIFIED")
            ]
        };

        var settings = new StubSettingProvider();

        var service = new ComplianceDeviceListService(
            devices,
            settings
        );

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.GetAsync()
        );

        Assert.Equal(0, settings.CallCount);
    }

    [Fact]
    public async Task GetAsync_SettingCollectionFailurePropagates()
    {
        var settings = new StubSettingProvider
        {
            Failure = new HttpRequestException(
                "Synthetic setting collection failure"
            )
        };

        var service = new ComplianceDeviceListService(
            new StubDeviceProvider(),
            settings
        );

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetAsync()
        );

        Assert.Equal(1, settings.CallCount);
    }

    [Fact]
    public async Task GetAsync_DeviceCollectionFailureStopsReview()
    {
        var devices = new StubDeviceProvider
        {
            Failure = new HttpRequestException(
                "Synthetic device collection failure"
            )
        };

        var settings = new StubSettingProvider();

        var service = new ComplianceDeviceListService(
            devices,
            settings
        );

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetAsync()
        );

        Assert.Equal(1, devices.CallCount);
        Assert.Equal(0, settings.CallCount);
    }

    [Fact]
    public async Task GetAsync_EmptyInventoryDoesNotDiscardSettings()
    {
        var devices = new StubDeviceProvider
        {
            Devices = []
        };

        var settings = new StubSettingProvider
        {
            Evidence =
            [
                Setting("device-999", "Encryption")
            ]
        };

        var service = new ComplianceDeviceListService(
            devices,
            settings
        );

        var result = await service.GetAsync();

        Assert.Empty(result.Entries);
        Assert.Single(result.UnmatchedSettings);

        Assert.Equal(0, result.MatchedSettingCount);
        Assert.Equal(1, result.RetrievedSettingCount);
    }

    private static ManagedDevice Device(
        string? id,
        string name,
        string state = "noncompliant")
    {
        return new ManagedDevice
        {
            Id = id,
            DeviceName = name,
            ComplianceState = state
        };
    }

    private static ComplianceSettingEvidence Setting(
        string? deviceId,
        string name,
        string state = "nonCompliant")
    {
        return new ComplianceSettingEvidence(
            "summary-001",
            name,
            deviceId,
            "SYNTHETIC-DEVICE",
            state
        );
    }

    private sealed class StubDeviceProvider
        : IGraphManagedDeviceProvider
    {
        public int CallCount { get; private set; }

        public Exception? Failure { get; init; }

        public IReadOnlyList<ManagedDevice> Devices
        {
            get;
            init;
        } =
        [
            Device("device-001", "STALE-01"),
            Device("device-002", "UNKNOWN-02", "unknown")
        ];

        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Devices);
        }
    }

    private sealed class StubSettingProvider
        : IGraphComplianceSettingProvider
    {
        public int CallCount { get; private set; }

        public Exception? Failure { get; init; }

        public IReadOnlyList<ComplianceSettingEvidence>
            Evidence { get; init; } =
        [
            Setting("DEVICE-001", "Encryption")
        ];

        public Task<IReadOnlyList<ComplianceSettingEvidence>>
            GetSettingEvidenceAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(Evidence);
        }
    }
}
