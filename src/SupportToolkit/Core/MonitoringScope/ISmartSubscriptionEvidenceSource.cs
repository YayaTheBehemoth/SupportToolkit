namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Normalized subscription evidence for entitlement evaluation.
///
/// Complete or the read failed: a source never returns a partial
/// collection.
/// </summary>
public sealed record SmartSubscriptionEvidence(
    IReadOnlyList<SmartSubscriptionHeader> Headers,
    IReadOnlyList<SmartSubscriptionLine> Lines
);

/// <summary>
/// Shared monitoring-scope capability: supplies subscription evidence
/// to any module that needs to scope its work to entitled customers.
///
/// Providers implement this contract. Modules consume the evidence
/// with SmartSubscriptionEntitlementEvaluator, supplying their own
/// monitoring item number, and decide how the result narrows their
/// own targets.
/// </summary>
public interface ISmartSubscriptionEvidenceSource
{
    Task<SmartSubscriptionEvidence> GetEvidenceAsync(
        CancellationToken cancellationToken = default
    );
}
