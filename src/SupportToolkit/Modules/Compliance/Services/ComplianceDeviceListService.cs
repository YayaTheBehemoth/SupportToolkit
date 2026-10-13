using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Services;

public sealed record ComplianceDeviceListEntry(
    ManagedDevice Device,
    IReadOnlyList<ComplianceSettingEvidence> Settings
);

public sealed record ComplianceDeviceListResult(
    IReadOnlyList<ComplianceDeviceListEntry> Entries
);

/// <summary>
/// Retrieves the device inventory and candidate setting evidence once each.
///
/// This correlation is currently supported by synthetic fixtures only.
/// Errors in either source fail the review instead of concealing gaps.
/// </summary>
public sealed class ComplianceDeviceListService
{
    private readonly IGraphManagedDeviceProvider _deviceProvider;
    private readonly IGraphComplianceSettingProvider _settingProvider;

    public ComplianceDeviceListService(
        IGraphManagedDeviceProvider deviceProvider,
        IGraphComplianceSettingProvider settingProvider)
    {
        _deviceProvider = deviceProvider
            ?? throw new ArgumentNullException(nameof(deviceProvider));
        _settingProvider = settingProvider
            ?? throw new ArgumentNullException(nameof(settingProvider));
    }

    public async Task<ComplianceDeviceListResult> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = await _deviceProvider
            .GetManagedDevicesAsync(cancellationToken);

        var evidence = await _settingProvider
            .GetSettingEvidenceAsync(cancellationToken);

        var byDeviceId = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.DeviceId))
            .ToLookup(
                item => item.DeviceId!,
                StringComparer.OrdinalIgnoreCase
            );

        var entries = new List<ComplianceDeviceListEntry>();

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                throw new InvalidDataException(
                    "Managed device is missing its Graph ID."
                );
            }

            entries.Add(
                new ComplianceDeviceListEntry(
                    device,
                    byDeviceId[device.Id].ToList().AsReadOnly()
                )
            );
        }

        return new ComplianceDeviceListResult(entries.AsReadOnly());
    }
}
