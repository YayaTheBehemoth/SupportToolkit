
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Services;

public sealed record ComplianceDeviceListEntry(
    ManagedDevice Device,
    IReadOnlyList<ComplianceSettingEvidence> Settings
);

public sealed record ComplianceDeviceListResult(
    IReadOnlyList<ComplianceDeviceListEntry> Entries
)
{
    // Existing callers can still construct the result
    // with only its device entries.
    public IReadOnlyList<ComplianceSettingEvidence>
        UnmatchedSettings { get; init; } = [];

    public int MatchedSettingCount =>
        Entries.Sum(entry => entry.Settings.Count);

    public int RetrievedSettingCount =>
        MatchedSettingCount + UnmatchedSettings.Count;
}

/// <summary>
/// Retrieves managed devices and setting evidence once each.
///
/// The device-ID correlation remains a synthetic hypothesis.
/// Duplicate inventory identities fail the review.
/// Unmatched evidence remains visible in collection diagnostics.
///
/// Any provider failure propagates to the caller.
/// No partial-success result is returned.
/// </summary>
public sealed class ComplianceDeviceListService
{
    private readonly IGraphManagedDeviceProvider
        _deviceProvider;

    private readonly IGraphComplianceSettingProvider
        _settingProvider;

    public ComplianceDeviceListService(
        IGraphManagedDeviceProvider deviceProvider,
        IGraphComplianceSettingProvider settingProvider)
    {
        _deviceProvider = deviceProvider
            ?? throw new ArgumentNullException(
                nameof(deviceProvider)
            );

        _settingProvider = settingProvider
            ?? throw new ArgumentNullException(
                nameof(settingProvider)
            );
    }

    public async Task<ComplianceDeviceListResult> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = await _deviceProvider
            .GetManagedDevicesAsync(cancellationToken);

        // Validate identity before retrieving more data.
        // Duplicate IDs cannot safely be interpreted
        // as separate managed devices.
        var knownDeviceIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id))
            {
                throw new InvalidDataException(
                    "Managed device is missing its Graph ID."
                );
            }

            if (!knownDeviceIds.Add(device.Id.Trim()))
            {
                throw new InvalidDataException(
                    "Duplicate managed-device ID returned. " +
                    "Compliance inventory review aborted."
                );
            }
        }

        var evidence = await _settingProvider
            .GetSettingEvidenceAsync(cancellationToken);

        // Only non-empty identifiers can participate
        // in the current synthetic correlation.
        var byDeviceId = evidence
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.DeviceId))
            .ToLookup(
                item => item.DeviceId!.Trim(),
                StringComparer.OrdinalIgnoreCase
            );

        var entries = new List<ComplianceDeviceListEntry>();

        foreach (var device in devices)
        {
            var matched = byDeviceId[device.Id!.Trim()]
                .ToList()
                .AsReadOnly();

            entries.Add(
                new ComplianceDeviceListEntry(
                    device,
                    matched
                )
            );
        }

        // Do not silently discard setting evidence
        // whose device ID was absent from the inventory,
        // or whose device ID was never provided.
        var unmatched = evidence
            .Where(item =>
                string.IsNullOrWhiteSpace(item.DeviceId)
                || !knownDeviceIds.Contains(
                    item.DeviceId.Trim()
                ))
            .ToList()
            .AsReadOnly();

        var result = new ComplianceDeviceListResult(
            entries.AsReadOnly()
        )
        {
            UnmatchedSettings = unmatched
        };

        // Every retrieved setting row must be accounted for.
        // This validates our processing, not the completeness
        // of the underlying Microsoft Graph response.
        if (result.RetrievedSettingCount != evidence.Count)
        {
            throw new InvalidDataException(
                "Compliance setting evidence accounting failed."
            );
        }

        return result;
    }
}
