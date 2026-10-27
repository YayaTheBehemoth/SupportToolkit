
namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Normalized SMARTsubscription header evidence.
///
/// Observed source:
/// Subscription header record.
///
/// The exact Business Central API representation is not known.
///
/// CustomerNumber is the sell-to customer, used as a provisional
/// monitoring customer. This choice is not validated business logic;
/// the header field that identifies the customer is still to be
/// confirmed from the API page's $metadata.
/// BillToCustomerNumber is preserved so a difference can be reviewed.
///
/// BlockedValue is the raw "blocked" evidence. Null means the source
/// did not report it, which is treated as missing evidence.
/// </summary>
public sealed record SmartSubscriptionHeader(
    string SubscriptionNumber,
    string? CustomerNumber,
    string Status,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? BillToCustomerNumber = null,
    string? BlockedValue = null
);

/// <summary>
/// Normalized SMARTsubscription line evidence.
///
/// Observed source:
/// Subscription line record.
///
/// SubscriptionNumber corresponds to the source field "Nr.".
/// ItemNumber corresponds to the source field "Nummer".
///
/// LineType and OmitLine are raw source evidence. Null means the
/// source did not report the value, which is treated as missing
/// evidence. The meaning of OmitLine is not yet established.
/// </summary>
public sealed record SmartSubscriptionLine(
    string SubscriptionNumber,
    string ItemNumber,
    string? LineType = null,
    bool? OmitLine = null
);

/// <summary>
/// Language-specific status texts the evaluator recognizes.
///
/// The source status column is language dependent. English is the
/// pinned request language; observed values were "Active",
/// "Suspended" and "Open". "Open" is recognized as a value so it can
/// be reported, but its meaning is unverified and no policy treats it
/// as active or inactive.
/// </summary>
public sealed record SmartSubscriptionStatusVocabulary(
    string Active,
    string Suspended,
    string? Open = null)
{
    public static SmartSubscriptionStatusVocabulary English { get; } =
        new("Active", "Suspended", "Open");
}

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
/// ItemNumber is the entitling item on the subscription's single
/// monitoring line, set once that line has been validated. It is null
/// when the evidence was rejected before the line was established.
///
/// No Acronis identity mapping is performed here.
/// </summary>
public sealed record MonitoringEntitlementObservation(
    string SubscriptionNumber,
    string? CustomerNumber,
    MonitoringEntitlementAssessment Assessment,
    string Reason,
    bool IsSuspended = false,
    string? ItemNumber = null,
    bool IsBlocked = false
);
