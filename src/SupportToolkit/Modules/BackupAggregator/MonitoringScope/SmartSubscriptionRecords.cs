
namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Normalized SMARTsubscription header evidence.
///
/// Observed source:
/// SMART Subscription Header, table 71581506.
///
/// The exact Business Central API representation is not known.
/// </summary>
public sealed record SmartSubscriptionHeader(
    string SubscriptionNumber,
    string? CustomerNumber,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate
);

/// <summary>
/// Normalized SMARTsubscription line evidence.
///
/// Observed source:
/// SMART Subscription Line, table 71581507.
///
/// SubscriptionNumber corresponds to the source field "Nr.".
/// ItemNumber corresponds to the source field "Nummer".
/// </summary>
public sealed record SmartSubscriptionLine(
    string SubscriptionNumber,
    string ItemNumber
);

/// <summary>
/// Provisional assessment of subscription evidence.
///
/// This is not a final operational monitoring decision.
/// </summary>
public enum MonitoringEntitlementAssessment
{
    ActiveCandidate,
    NeedsReview
}

/// <summary>
/// One evaluated monitoring subscription.
///
/// No Acronis identity mapping is performed here.
/// </summary>
public sealed record MonitoringEntitlementObservation(
    string SubscriptionNumber,
    string? CustomerNumber,
    MonitoringEntitlementAssessment Assessment,
    string Reason
);
