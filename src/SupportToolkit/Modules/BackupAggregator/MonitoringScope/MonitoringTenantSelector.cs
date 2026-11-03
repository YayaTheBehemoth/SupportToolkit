using SupportToolkit.Core.MonitoringScope;

using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// A resolved relationship between a Business Central customer
/// and an Acronis tenant.
///
/// This is input to selection, not a manually maintained registry.
/// The resolution mechanism remains a separate discovery problem.
/// </summary>
public sealed record ResolvedMonitoringCustomerIdentity(
    string BusinessCentralCustomerNumber,
    string AcronisTenantId
);

public enum MonitoringTenantSelectionStatus
{
    CandidateForInventory,
    NeedsReview,
    Unclassified,

    /// <summary>
    /// Resolved customer without an Active entitling subscription:
    /// no entitling item at all, or only on Suspended subscriptions.
    /// A decided outcome, not an open question.
    /// </summary>
    NotEntitled
}

/// <summary>
/// The subscription and item that entitle a customer to monitoring.
/// </summary>
public sealed record EntitlingSubscription(
    string SubscriptionNumber,
    string? ItemNumber
);

public sealed record MonitoringTenantSelectionFinding(
    TenantDto Tenant,
    MonitoringTenantSelectionStatus Status,
    string Reason)
{
    /// <summary>
    /// The resolved Business Central customer, when one is known.
    /// </summary>
    public string? CustomerNumber { get; init; }

    /// <summary>
    /// True for the operator's own customer-kind tenant. It is billed
    /// to no one, so it is always included instead of being decided
    /// by a Business Central entitlement.
    /// </summary>
    public bool IsOperatorTenant { get; init; }

    /// <summary>
    /// What put an inventory candidate in scope. Empty otherwise.
    /// </summary>
    public IReadOnlyList<EntitlingSubscription> EntitlingSubscriptions
    {
        get;
        init;
    } = [];
}

public enum MonitoringTenantExclusionReason
{
    /// <summary>
    /// Listed in the profile's excluded tenant IDs.
    /// </summary>
    Configured
}

/// <summary>
/// A customer tenant deliberately left out before selection.
/// </summary>
public sealed record ExcludedMonitoringTenant(
    TenantDto Tenant,
    MonitoringTenantExclusionReason Reason
);

/// <summary>
/// Complete tenant-selection evidence.
///
/// Every supplied Acronis customer tenant remains represented.
/// Unmatched Business Central observations remain visible separately.
/// </summary>
public sealed record MonitoringTenantSelectionResult(
    IReadOnlyList<MonitoringTenantSelectionFinding> Tenants,
    IReadOnlyList<MonitoringEntitlementObservation> UnmatchedEvidence)
{
    /// <summary>
    /// Customer tenants deliberately left out of the selection, such as
    /// the operator's own tenant.
    /// </summary>
    public IReadOnlyList<ExcludedMonitoringTenant> ExcludedTenants
    {
        get;
        init;
    } = [];

    public int ExcludedTenantCount =>
        ExcludedTenants.Count;

    public int NotEntitledCount =>
        Tenants.Count(
            finding =>
                finding.Status
                == MonitoringTenantSelectionStatus.NotEntitled
        );

    /// <summary>
    /// Configuration problems that make the selection untrustworthy.
    /// Any notice keeps the scope from being reported as complete.
    /// </summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    public IReadOnlyList<TenantDto> InventoryCandidates =>
        Tenants
            .Where(
                finding =>
                    finding.Status
                    == MonitoringTenantSelectionStatus.CandidateForInventory
            )
            .Select(
                finding => finding.Tenant
            )
            .ToArray();

    public int NeedsReviewCount =>
        Tenants.Count(
            finding =>
                finding.Status
                == MonitoringTenantSelectionStatus.NeedsReview
        );

    public int UnclassifiedCount =>
        Tenants.Count(
            finding =>
                finding.Status
                == MonitoringTenantSelectionStatus.Unclassified
        );
}

/// <summary>
/// Combines entitlement observations and resolved identities
/// to identify candidate Acronis tenants.
///
/// Selection is deliberately conservative:
/// conflicting, missing or unresolved evidence is never silently
/// interpreted as permission to exclude a customer.
///
/// Not connected to the production inventory workflow.
/// </summary>
public sealed class MonitoringTenantSelector
{
    public MonitoringTenantSelectionResult Select(
        IReadOnlyList<TenantDto> acronisTenants,
        IReadOnlyList<MonitoringEntitlementObservation> observations,
        IReadOnlyList<ResolvedMonitoringCustomerIdentity> identities,
        IReadOnlySet<string>? excludedTenantIds = null)
    {
        ArgumentNullException.ThrowIfNull(acronisTenants);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(identities);

        // Excluded tenants are not customers: they are neither
        // candidates nor unclassified.
        var configuredExclusions =
            (excludedTenantIds ?? new HashSet<string>())
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excluded =
            new HashSet<string>(
                configuredExclusions,
                StringComparer.OrdinalIgnoreCase);

        var notices =
            new List<string>();

        var detection =
            OperatorTenantDetector.Detect(acronisTenants);

        var ownId =
            detection.TenantId;

        // The operator's own customer-kind tenant, monitored but
        // billed to no one. Null when none is recognized, when it has
        // a Business Central identity, or when it is excluded by
        // configuration, which always wins.
        string? operatorTenantId = null;

        var ownTenantReviewed = false;

        if (detection.Notice is not null)
        {
            notices.Add(detection.Notice);
        }

        if (!string.IsNullOrEmpty(ownId))
        {
            var hasBusinessCentralLink =
                identities.Any(
                    identity => string.Equals(
                        identity.AcronisTenantId?.Trim(),
                        ownId,
                        StringComparison.OrdinalIgnoreCase));

            if (hasBusinessCentralLink)
            {
                // Our own company should have no Business Central
                // customer; a link means the detection is probably
                // wrong. The tenant is then judged like any customer.
                ownTenantReviewed = true;

                notices.Add(
                    "The tenant matching the partner name has a " +
                    "Business Central identity, so it was not treated " +
                    "as the operator's own.");
            }
            else if (!configuredExclusions.Contains(ownId))
            {
                operatorTenantId = ownId;
            }
        }

        var customers =
            acronisTenants
                .Where(
                    tenant => !excluded.Contains(tenant.Id?.Trim() ?? string.Empty))
                .Where(
                    tenant => !string.Equals(
                        tenant.Id?.Trim(),
                        operatorTenantId,
                        StringComparison.OrdinalIgnoreCase))
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.Kind,
                            "customer",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .OrderBy(
                    tenant => tenant.Name,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(
                    tenant => tenant.Id,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();

        var duplicateTenantIds =
            customers
                .GroupBy(
                    tenant => tenant.Id,
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(
                    group => group.Count() > 1
                )
                .Select(
                    group => group.Key
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        var findings =
            new List<MonitoringTenantSelectionFinding>();

        foreach (var tenant in customers)
        {
            if (ownTenantReviewed
                && string.Equals(
                    tenant.Id?.Trim(),
                    ownId,
                    StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Tenant matching the partner name has a Business " +
                        "Central identity; not treated as the operator's own."
                    )
                );

                continue;
            }

            if (!Guid.TryParse(tenant.Id, out _))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Acronis tenant has no valid UUID."
                    )
                );

                continue;
            }

            if (duplicateTenantIds.Contains(tenant.Id))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Acronis tenant UUID occurs more than once."
                    )
                );

                continue;
            }

            var matches =
                identities
                    .Where(
                        identity =>
                            string.Equals(
                                identity.AcronisTenantId?.Trim(),
                                tenant.Id,
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .ToArray();

            if (matches.Length == 0)
            {
                findings.Add(
                    Unclassified(
                        tenant,
                        "No resolved Business Central customer identity."
                    )
                );

                continue;
            }

            if (matches.Length != 1)
            {
                findings.Add(
                    Review(
                        tenant,
                        "Multiple identity relationships require investigation."
                    )
                );

                continue;
            }

            var customerNumber =
                matches[0].BusinessCentralCustomerNumber?.Trim();

            if (string.IsNullOrWhiteSpace(customerNumber))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Resolved identity has no Business Central customer number."
                    )
                );

                continue;
            }

            var customerEvidence =
                observations
                    .Where(
                        observation =>
                            string.Equals(
                                observation.CustomerNumber?.Trim(),
                                customerNumber,
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .ToArray();

            if (customerEvidence.Length == 0)
            {
                findings.Add(
                    NotEntitled(
                        tenant,
                        customerNumber,
                        "No subscription with an entitling item."
                    )
                );

                continue;
            }

            // An entitling item only on Suspended subscriptions does
            // not entitle the customer.
            if (customerEvidence.All(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.NeedsReview
                        && observation.IsSuspended))
            {
                findings.Add(
                    NotEntitled(
                        tenant,
                        customerNumber,
                        "Entitling item only on suspended subscriptions."
                    )
                );

                continue;
            }

            /*
             * Entitlement is per customer: a clean Active subscription
             * decides, and a cleanly Suspended sibling does not override
             * it. Any other review reason still blocks the decision.
             */
            var hasActive =
                customerEvidence.Any(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.ActiveCandidate);

            if (customerEvidence.Any(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.NeedsReview
                        && !(hasActive && observation.IsSuspended)))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Customer has monitoring entitlement evidence requiring review."
                    ) with
                    {
                        CustomerNumber = customerNumber
                    }
                );

                continue;
            }

            if (hasActive)
            {
                findings.Add(
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Active monitoring evidence and resolved customer identity."
                    )
                    {
                        CustomerNumber = customerNumber,

                        EntitlingSubscriptions =
                            customerEvidence
                                .Where(
                                    observation =>
                                        observation.Assessment
                                        == MonitoringEntitlementAssessment
                                            .ActiveCandidate)
                                .Select(
                                    observation =>
                                        new EntitlingSubscription(
                                            observation.SubscriptionNumber,
                                            observation.ItemNumber))
                                .OrderBy(
                                    subscription =>
                                        subscription.SubscriptionNumber,
                                    StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                    }
                );

                continue;
            }

            findings.Add(
                Unclassified(
                    tenant,
                    "Monitoring entitlement classification is unknown."
                )
            );
        }

        /*
         * The operator's own company is monitored too but billed to no
         * one, so it has no Business Central entitlement and is always
         * included. Its customer-kind tenant is the one reviewed: the
         * partner tenant is not reviewable and only identifies it. A
         * configured exclusion still wins.
         */
        var operatorTenant =
            operatorTenantId is null
                ? null
                : acronisTenants.FirstOrDefault(
                    tenant => string.Equals(
                            tenant.Id?.Trim(),
                            operatorTenantId,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            tenant.Kind,
                            "customer",
                            StringComparison.OrdinalIgnoreCase));

        if (operatorTenant is not null)
        {
            findings.Add(
                Guid.TryParse(operatorTenant.Id, out _)
                    ? new MonitoringTenantSelectionFinding(
                        operatorTenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Operator's own tenant; not billed, so it is " +
                        "always included."
                    )
                    {
                        IsOperatorTenant = true
                    }
                    : Review(
                        operatorTenant,
                        "Operator's own tenant has no valid UUID."
                    )
            );
        }

        /*
         * An entitlement observation without a resolved relationship
         * to an existing Acronis customer tenant must remain visible.
         *
         * A link to a nonexistent Acronis tenant does not count
         * as a successful match.
         */
        var existingTenantIds =
            customers
                .Select(
                    tenant => tenant.Id
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        var linkedCustomerNumbers =
            identities
                .Where(
                    identity =>
                        existingTenantIds.Contains(
                            identity.AcronisTenantId?.Trim()
                            ?? string.Empty
                        )
                )
                .Select(
                    identity =>
                        identity.BusinessCentralCustomerNumber?.Trim()
                )
                .Where(
                    customerNumber =>
                        !string.IsNullOrWhiteSpace(customerNumber)
                )
                .Select(
                    customerNumber => customerNumber!
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        // A customer whose subscriptions are all suspended or blocked
        // has no service to monitor, so having no tenant is expected
        // and is not unmatched evidence.
        var customerStates =
            CustomerEntitlementActivity.Classify(observations);

        var unmatchedEvidence =
            observations
                .Where(
                    observation =>
                        string.IsNullOrWhiteSpace(
                            observation.CustomerNumber
                        )
                        || (!linkedCustomerNumbers.Contains(
                                observation.CustomerNumber.Trim())
                            && customerStates[
                                observation.CustomerNumber.Trim()]
                                != CustomerEntitlementState.Inactive)
                )
                .ToArray();

        // Only tenants excluded by configuration are left out.
        var excludedTenants =
            acronisTenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.Kind,
                            "customer",
                            StringComparison.OrdinalIgnoreCase)
                        && excluded.Contains(tenant.Id?.Trim() ?? string.Empty))
                .OrderBy(
                    tenant => tenant.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    tenant =>
                        new ExcludedMonitoringTenant(
                            tenant,
                            MonitoringTenantExclusionReason.Configured))
                .ToArray();

        return new MonitoringTenantSelectionResult(
            findings.AsReadOnly(),
            unmatchedEvidence
        )
        {
            ExcludedTenants = excludedTenants,
            Notices = notices.AsReadOnly()
        };
    }

    private static MonitoringTenantSelectionFinding NotEntitled(
        TenantDto tenant,
        string customerNumber,
        string reason)
    {
        return new MonitoringTenantSelectionFinding(
            tenant,
            MonitoringTenantSelectionStatus.NotEntitled,
            reason
        )
        {
            CustomerNumber = customerNumber
        };
    }

    private static MonitoringTenantSelectionFinding Review(
        TenantDto tenant,
        string reason)
    {
        return new MonitoringTenantSelectionFinding(
            tenant,
            MonitoringTenantSelectionStatus.NeedsReview,
            reason
        );
    }

    private static MonitoringTenantSelectionFinding Unclassified(
        TenantDto tenant,
        string reason)
    {
        return new MonitoringTenantSelectionFinding(
            tenant,
            MonitoringTenantSelectionStatus.Unclassified,
            reason
        );
    }
}
