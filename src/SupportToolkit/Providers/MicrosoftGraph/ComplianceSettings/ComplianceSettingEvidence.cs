
namespace SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

/// <summary>
/// Source-reported setting state. Its relationship to a
/// managedDevice ID remains unverified in live Intune.
/// </summary>
public sealed record ComplianceSettingEvidence(
    string SummaryId,
    string SettingName,
    string? DeviceId,
    string? DeviceName,
    string? State
);
