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
    Unclassified
}

public sealed record MonitoringTenantSelectionFinding(
    TenantDto Tenant,
    MonitoringTenantSelectionStatus Status,
    string Reason
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
    public int ExcludedTenantCount { get; init; }

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
        var excluded =
            (excludedTenantIds ?? new HashSet<string>())
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var notices =
            new List<string>();

        var detection =
            OperatorTenantDetector.Detect(acronisTenants);

        var ownId =
            detection.TenantId;

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
                // wrong. The tenant is kept and reviewed, never hidden.
                ownTenantReviewed = true;

                notices.Add(
                    "The tenant matching the partner name has a " +
                    "Business Central identity and was not excluded.");
            }
            else
            {
                excluded.Add(ownId);
            }
        }

        var customers =
            acronisTenants
                .Where(
                    tenant => !excluded.Contains(tenant.Id?.Trim() ?? string.Empty))
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
                    Unclassified(
                        tenant,
                        "No monitoring entitlement evidence for the resolved customer."
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
                    )
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

        var unmatchedEvidence =
            observations
                .Where(
                    observation =>
                        string.IsNullOrWhiteSpace(
                            observation.CustomerNumber
                        )
                        || !linkedCustomerNumbers.Contains(
                            observation.CustomerNumber.Trim()
                        )
                )
                .ToArray();

        var excludedCount =
            acronisTenants.Count(
                tenant =>
                    string.Equals(
                        tenant.Kind,
                        "customer",
                        StringComparison.OrdinalIgnoreCase)
                    && excluded.Contains(tenant.Id?.Trim() ?? string.Empty));

        return new MonitoringTenantSelectionResult(
            findings.AsReadOnly(),
            unmatchedEvidence
        )
        {
            ExcludedTenantCount = excludedCount,
            Notices = notices.AsReadOnly()
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
