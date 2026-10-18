
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Fixtures;

/// <summary>
/// Synthetic multi-tenant dataset for the reporting MVP.
///
/// Names and IDs have no relationship to real customers.
/// Each tenant receives independent provider instances.
/// </summary>
public static class ComplianceSyntheticTenantFixtures
{
    public static IReadOnlyList<ComplianceTenantSource> Create(
        TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System)
            .GetUtcNow();

        return
        [
            CreateTenant(
                "synthetic-alpha",
                "Tenant Alpha",
                [
                    Device(
                        "shared-device-001",
                        "STK-SYNTHETIC-A-STALE-01",
                        "noncompliant",
                        now.AddDays(-74)
                    ),
                    Device(
                        "alpha-device-002",
                        "STK-SYNTHETIC-A-UNKNOWN-02",
                        "unknown",
                        null
                    ),
                    Device(
                        "alpha-device-003",
                        "STK-SYNTHETIC-A-HEALTHY-03",
                        "compliant",
                        now.AddHours(-2)
                    )
                ],
                [
                    Setting(
                        "alpha-encryption",
                        "Device encryption required",
                        "shared-device-001",
                        "nonCompliant"
                    ),
                    Setting(
                        "alpha-os",
                        "Minimum operating system version",
                        "shared-device-001",
                        "unknown"
                    ),
                    Setting(
                        "alpha-encryption",
                        "Device encryption required",
                        "alpha-device-003",
                        "compliant"
                    )
                ]
            ),

            CreateTenant(
                "synthetic-beta",
                "Tenant Beta",
                [
                    // Intentionally identical to an Alpha device ID.
                    // The setting evidence must not cross scopes.
                    Device(
                        "shared-device-001",
                        "STK-SYNTHETIC-B-FAILED-01",
                        "noncompliant",
                        now.AddDays(-2)
                    ),
                    Device(
                        "beta-device-002",
                        "STK-SYNTHETIC-B-HEALTHY-02",
                        "compliant",
                        now.AddHours(-3)
                    ),
                    Device(
                        "beta-device-003",
                        "STK-SYNTHETIC-B-CONFLICT-03",
                        "compliant",
                        now.AddHours(-1)
                    )
                ],
                [
                    Setting(
                        "beta-os",
                        "Operating system requirement",
                        "shared-device-001",
                        "nonCompliant"
                    ),
                    Setting(
                        "beta-os",
                        "Operating system requirement",
                        "beta-device-002",
                        "compliant"
                    ),
                    Setting(
                        "beta-encryption",
                        "Device encryption required",
                        "beta-device-003",
                        "nonCompliant"
                    ),
                    // Unmatched evidence: no invented device.
                    Setting(
                        "beta-orphan",
                        "Unmatched compliance requirement",
                        "beta-device-not-in-inventory",
                        "unknown"
                    )
                ]
            ),

            CreateTenant(
                "synthetic-gamma",
                "Tenant Gamma",
                [
                    Device(
                        "gamma-device-001",
                        "STK-SYNTHETIC-G-HEALTHY-01",
                        "compliant",
                        now.AddHours(-2)
                    ),
                    Device(
                        "gamma-device-002",
                        "STK-SYNTHETIC-G-HEALTHY-02",
                        "compliant",
                        now.AddHours(-4)
                    )
                ],
                [
                    Setting(
                        "gamma-encryption",
                        "Device encryption required",
                        "gamma-device-001",
                        "compliant"
                    ),
                    Setting(
                        "gamma-os",
                        "Operating system requirement",
                        "gamma-device-002",
                        "compliant"
                    )
                ]
            )
        ];
    }

    private static ComplianceTenantSource CreateTenant(
        string key,
        string name,
        IReadOnlyList<ManagedDevice> devices,
        IReadOnlyList<ComplianceSettingEvidence> evidence)
    {
        return new ComplianceTenantSource(
            new ComplianceTenantScope(key, name),
            new FixtureDeviceProvider(devices),
            new FixtureSettingProvider(evidence)
        );
    }

    private static ManagedDevice Device(
        string id,
        string name,
        string state,
        DateTimeOffset? lastSync)
    {
        return new ManagedDevice
        {
            Id = id,
            DeviceName = name,
            OperatingSystem = "Windows",
            ComplianceState = state,
            LastSyncDateTime = lastSync
        };
    }

    private static ComplianceSettingEvidence Setting(
        string summaryId,
        string name,
        string deviceId,
        string state)
    {
        return new ComplianceSettingEvidence(
            summaryId,
            name,
            deviceId,
            null,
            state
        );
    }

    private sealed class FixtureDeviceProvider
        : IGraphManagedDeviceProvider
    {
        private readonly IReadOnlyList<ManagedDevice> _devices;

        public FixtureDeviceProvider(
            IReadOnlyList<ManagedDevice> devices)
        {
            _devices = devices;
        }

        public Task<IReadOnlyList<ManagedDevice>>
            GetManagedDevicesAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_devices);
        }
    }

    private sealed class FixtureSettingProvider
        : IGraphComplianceSettingProvider
    {
        private readonly IReadOnlyList<ComplianceSettingEvidence>
            _settings;

        public FixtureSettingProvider(
            IReadOnlyList<ComplianceSettingEvidence> settings)
        {
            _settings = settings;
        }

        public Task<IReadOnlyList<ComplianceSettingEvidence>>
            GetSettingEvidenceAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings);
        }
    }
}
