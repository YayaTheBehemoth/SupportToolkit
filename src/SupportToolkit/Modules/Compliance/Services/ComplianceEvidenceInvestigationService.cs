
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Services;

public sealed record ComplianceEvidenceInvestigation(
    ManagedDevice Device,
    IReadOnlyList<ComplianceSettingEvidence> MatchedSettings
);

/// <summary>
/// Combines existing device lookup with candidate
/// compliance-setting evidence.
///
/// The device ID correlation is a synthetic assumption,
/// not a confirmed live Microsoft Graph relationship.
/// </summary>
public sealed class ComplianceEvidenceInvestigationService
{
    private readonly ComplianceInvestigationService
        _deviceInvestigation;

    private readonly IGraphComplianceSettingProvider
        _settingProvider;

    public ComplianceEvidenceInvestigationService(
        ComplianceInvestigationService deviceInvestigation,
        IGraphComplianceSettingProvider settingProvider)
    {
        _deviceInvestigation = deviceInvestigation
            ?? throw new ArgumentNullException(
                nameof(deviceInvestigation)
            );

        _settingProvider = settingProvider
            ?? throw new ArgumentNullException(
                nameof(settingProvider)
            );
    }

    public async Task<ComplianceEvidenceInvestigation?>
        InvestigateAsync(
            string deviceName,
            CancellationToken cancellationToken = default)
    {
        var device = await _deviceInvestigation
            .InvestigateAsync(
                deviceName,
                cancellationToken
            );

        if (device is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(device.Id))
        {
            throw new InvalidDataException(
                "Cannot correlate a device without an ID."
            );
        }

        var allEvidence = await _settingProvider
            .GetSettingEvidenceAsync(cancellationToken);

        var matches = allEvidence
            .Where(setting => string.Equals(
                setting.DeviceId,
                device.Id,
                StringComparison.OrdinalIgnoreCase
            ))
            .ToList()
            .AsReadOnly();

        return new ComplianceEvidenceInvestigation(
            device,
            matches
        );
    }
}
