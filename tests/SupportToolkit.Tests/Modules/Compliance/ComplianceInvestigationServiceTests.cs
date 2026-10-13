
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceInvestigationServiceTests
{
    [Fact]
    public async Task InvestigateAsync_FindsDeviceByExactName()
    {
        var service = CreateService(
            new ManagedDevice
            {
                Id = "device-001",
                DeviceName = "STK-SYNTHETIC-STALE-01",
                ComplianceState = "noncompliant"
            },
            new ManagedDevice
            {
                Id = "device-002",
                DeviceName = "STK-SYNTHETIC-HEALTHY-02",
                ComplianceState = "compliant"
            }
        );

        var result = await service.InvestigateAsync(
            "stk-synthetic-stale-01"
        );

        Assert.NotNull(result);
        Assert.Equal("device-001", result.Id);
        Assert.Equal("noncompliant", result.ComplianceState);
    }

    [Fact]
    public async Task InvestigateAsync_ReturnsNullWhenNotFound()
    {
        var service = CreateService(
            new ManagedDevice
            {
                Id = "device-001",
                DeviceName = "DEVICE-01"
            }
        );

        var result = await service.InvestigateAsync(
            "UNKNOWN-DEVICE"
        );

        Assert.Null(result);
    }

    [Fact]
    public async Task InvestigateAsync_RejectsAmbiguousName()
    {
        var service = CreateService(
            new ManagedDevice
            {
                Id = "device-001",
                DeviceName = "DUPLICATE"
            },
            new ManagedDevice
            {
                Id = "device-002",
                DeviceName = "duplicate"
            }
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InvestigateAsync("DUPLICATE")
        );
    }

    private static ComplianceInvestigationService CreateService(
        params ManagedDevice[] devices)
    {
        return new ComplianceInvestigationService(
            new StubProvider(devices)
        );
    }

    private sealed class StubProvider
        : IGraphManagedDeviceProvider
    {
        private readonly IReadOnlyList<ManagedDevice> _devices;

        public StubProvider(IReadOnlyList<ManagedDevice> devices)
        {
            _devices = devices;
        }

        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_devices);
        }
    }
}
