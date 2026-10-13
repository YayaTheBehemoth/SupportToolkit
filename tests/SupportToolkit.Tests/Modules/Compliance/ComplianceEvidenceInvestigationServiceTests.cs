
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Modules.Compliance;

public class ComplianceEvidenceInvestigationServiceTests
{
    [Fact]
    public async Task InvestigateAsync_MatchesExactDeviceId()
    {
        var workflow = CreateWorkflow(
            new ComplianceSettingEvidence(
                "summary-001",
                "Encryption",
                "device-001",
                "DEMO",
                "nonCompliant"
            ),
            new ComplianceSettingEvidence(
                "summary-002",
                "OS version",
                "device-002",
                "OTHER",
                "compliant"
            )
        );

        var result = await workflow.InvestigateAsync(
            "DEMO"
        );

        Assert.NotNull(result);
        Assert.Equal("device-001", result.Device.Id);

        var setting = Assert.Single(result.MatchedSettings);

        Assert.Equal("Encryption", setting.SettingName);
        Assert.Equal("nonCompliant", setting.State);
    }

    [Fact]
    public async Task InvestigateAsync_NoMatchDoesNotInventCause()
    {
        var workflow = CreateWorkflow(
            new ComplianceSettingEvidence(
                "summary-001",
                "Encryption",
                "different-device",
                "DEMO",
                "nonCompliant"
            )
        );

        var result = await workflow.InvestigateAsync(
            "DEMO"
        );

        Assert.NotNull(result);
        Assert.Empty(result.MatchedSettings);
    }

    private static ComplianceEvidenceInvestigationService
        CreateWorkflow(
            params ComplianceSettingEvidence[] evidence)
    {
        var deviceProvider = new StubDeviceProvider();

        var deviceService =
            new ComplianceInvestigationService(
                deviceProvider
            );

        return new ComplianceEvidenceInvestigationService(
            deviceService,
            new StubSettingProvider(evidence)
        );
    }

    private sealed class StubDeviceProvider
        : IGraphManagedDeviceProvider
    {
        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ManagedDevice> devices =
            [
                new ManagedDevice
                {
                    Id = "device-001",
                    DeviceName = "DEMO",
                    ComplianceState = "noncompliant"
                }
            ];

            return Task.FromResult(devices);
        }
    }

    private sealed class StubSettingProvider
        : IGraphComplianceSettingProvider
    {
        private readonly IReadOnlyList<ComplianceSettingEvidence>
            _evidence;

        public StubSettingProvider(
            IReadOnlyList<ComplianceSettingEvidence> evidence)
        {
            _evidence = evidence;
        }

        public Task<IReadOnlyList<ComplianceSettingEvidence>>
            GetSettingEvidenceAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_evidence);
        }
    }
}
