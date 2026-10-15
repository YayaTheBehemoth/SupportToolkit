
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
        IReadOnlyList<ResolvedMonitoringCustomerIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(acronisTenants);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(identities);

        var customers =
            acronisTenants
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

            if (customerEvidence.Any(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.NeedsReview))
            {
                findings.Add(
                    Review(
                        tenant,
                        "Customer has monitoring entitlement evidence requiring review."
                    )
                );

                continue;
            }

            if (customerEvidence.All(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.ActiveCandidate))
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

        return new MonitoringTenantSelectionResult(
            findings.AsReadOnly(),
            unmatchedEvidence
        );
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
