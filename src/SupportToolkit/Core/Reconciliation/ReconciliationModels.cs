namespace SupportToolkit.Core.Reconciliation;

/// <summary>
/// Provider-neutral Acronis tenant evidence.
///
/// Enabled null means the source did not report it.
/// </summary>
public sealed record AcronisTenantObservation(
    string TenantId,
    bool? Enabled,
    bool Deleted
);

/// <summary>
/// Every reconciliation result lands in exactly one bucket.
///
/// Only MatchedDiscrepancy can ever lead to a ticket. Unscoped and
/// Ambiguous results belong in an exception queue for human review.
/// </summary>
public enum ReconciliationBucket
{
    MatchedConsistent,
    MatchedDiscrepancy,
    Unscoped,
    Ambiguous
}

/// <summary>
/// Provisional discrepancy types. Their business definitions are not
/// yet agreed; quantity versus usage is not implemented because no
/// usage evidence is available.
/// </summary>
public enum DiscrepancyType
{
    /// <summary>
    /// The Acronis tenant is enabled, but no in-scope subscription of
    /// the customer is active and at least one is Suspended or
    /// blocked for All.
    /// </summary>
    TenantActiveSubscriptionInactive,

    /// <summary>
    /// An in-scope subscription is Active and not blocked, but the
    /// Acronis tenant is absent, disabled or deleted. This is tenant
    /// level only; deployed services are not compared.
    /// </summary>
    SubscriptionActiveTenantMissing,

    /// <summary>
    /// The Acronis tenant is reported as monitored, but the mapped
    /// customer has no in-scope subscription with an entitled item.
    /// Requires monitored evidence, which has no production source yet.
    /// </summary>
    MonitoredWithoutEntitlement
}

public enum ReconciliationSubject
{
    Mapping,
    AcronisTenant,
    BcCustomer,
    BcSubscription
}

/// <summary>
/// Deduplication identity for a would-be ticket.
/// </summary>
public sealed record DiscrepancyKey(
    Guid MappingId,
    DiscrepancyType Type,
    string Period
);

public sealed record ReconciliationDiscrepancy(
    DiscrepancyKey Key,
    IReadOnlyList<string> SubscriptionNumbers,
    string Reason
);

/// <summary>
/// One reconciled subject.
///
/// SubjectId is the mapping id, tenant id, customer number or
/// subscription number, depending on Subject. For a mapping subject,
/// AcronisTenantId and BcCustomerNo identify both sides.
/// </summary>
public sealed record ReconciliationResult(
    ReconciliationBucket Bucket,
    ReconciliationSubject Subject,
    string SubjectId,
    Guid? MappingId,
    IReadOnlyList<ReconciliationDiscrepancy> Discrepancies,
    IReadOnlyList<string> Reasons)
{
    public string? AcronisTenantId { get; init; }

    public string? BcCustomerNo { get; init; }
}

public sealed record ReconciliationReport(
    string Period,
    IReadOnlyList<ReconciliationResult> Results)
{
    public int Count(
        ReconciliationBucket bucket) =>
        Results.Count(
            result => result.Bucket == bucket
        );

    /// <summary>
    /// Discrepancies that would become tickets if their type were
    /// enabled. Nothing here creates a ticket.
    /// </summary>
    public IReadOnlyList<ReconciliationDiscrepancy> WouldBeTickets =>
        Results
            .Where(
                result =>
                    result.Bucket == ReconciliationBucket.MatchedDiscrepancy
            )
            .SelectMany(
                result => result.Discrepancies
            )
            .ToArray();

    public int Count(
        DiscrepancyType type) =>
        WouldBeTickets.Count(
            discrepancy => discrepancy.Key.Type == type
        );
}
