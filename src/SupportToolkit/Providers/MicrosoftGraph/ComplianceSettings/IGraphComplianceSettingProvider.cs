
namespace SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

public interface IGraphComplianceSettingProvider
{
    Task<IReadOnlyList<ComplianceSettingEvidence>>
        GetSettingEvidenceAsync(
            CancellationToken cancellationToken = default
        );
}
