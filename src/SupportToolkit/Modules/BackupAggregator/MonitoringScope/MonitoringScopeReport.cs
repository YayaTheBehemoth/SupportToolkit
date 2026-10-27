using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

public enum ScopeExclusionReason
{
    Configured,
    NotEntitled
}

/// <summary>
/// A tenant reviewed this run and what put it in scope.
/// </summary>
public sealed record InScopeTenant(
    string TenantName,
    string TenantId,
    string? CustomerNumber,
    IReadOnlyList<EntitlingSubscription> EntitlingSubscriptions,
    bool IsOperatorTenant = false
);

/// <summary>
/// A customer tenant left out of the review, with the reason.
/// </summary>
public sealed record ExcludedScopeTenant(
    string TenantName,
    string TenantId,
    ScopeExclusionReason Reason,
    string Detail,
    string? CustomerNumber
);

/// <summary>
/// A tenant whose scope could not be decided.
/// </summary>
public sealed record UnresolvedScopeTenant(
    string TenantName,
    string TenantId,
    string? CustomerNumber,
    string Reason
);

/// <summary>
/// A discrepancy with the names needed to act on it.
/// </summary>
public sealed record ScopeDiscrepancy(
    string TenantName,
    string? TenantId,
    string? CustomerNumber,
    ReconciliationDiscrepancy Discrepancy
);

/// <summary>
/// An Ambiguous or Unscoped reconciliation result for the exception
/// queue.
/// </summary>
public sealed record ScopeException(
    ReconciliationBucket Bucket,
    string Subject,
    IReadOnlyList<string> Reasons
);

/// <summary>
/// Everything one scoped run decided, listed in full so the counts can
/// be checked against expectations.
///
/// The Acronis customer tenants are partitioned exactly once:
/// AcronisCustomerTenants = InScope + Excluded + NeedsReview +
/// UnscopedTenants.
///
/// Reconciliation results are optional; without them the discrepancy
/// and exception sections report that reconciliation was not run.
///
/// Tenant names are runtime data and appear only in runtime output.
/// </summary>
/// <summary>
/// A Business Central customer that no Acronis tenant maps to. State
/// is Active or NeedsReview: customers whose subscriptions are all
/// suspended or blocked are not gaps and never appear here.
/// </summary>
public sealed record ScopeCustomerGap(
    string CustomerNumber,
    string? CustomerName,
    CustomerEntitlementState State);

/// <summary>
/// What the automatic customer-to-tenant resolution decided this run.
///
/// Accepted links are recomputed every run and never stored. Pending
/// suggestions and exceptions are the cases left for a person.
/// </summary>
public sealed record AutomaticMappingSummary(
    IReadOnlyDictionary<CustomerMatchMethod, int> AcceptedByMethod,
    int PendingReview,
    int Exceptions,
    IReadOnlyList<ScopeCustomerGap> CustomersWithoutTenant,
    int CustomersWithoutName,
    int CustomersWithConflictingNames,
    string? NameFailure)
{
    public int Accepted =>
        AcceptedByMethod.Values.Sum();

    /// <summary>
    /// Entitled customers that hold none of the configured service
    /// items. The monitoring add-on implies the service itself, so
    /// each is a data inconsistency worth a look. Empty when no
    /// service items are configured.
    /// </summary>
    public IReadOnlyList<ScopeCustomerGap> EntitledWithoutServiceItem
    {
        get;
        init;
    } = [];
}

public sealed record MonitoringScopeReport(
    DateOnly AsOfDate,
    IReadOnlyList<string> EntitlingItems,
    int EntitledCustomers,
    IReadOnlyList<InScopeTenant> InScope,
    IReadOnlyList<ExcludedScopeTenant> Excluded,
    IReadOnlyList<UnresolvedScopeTenant> NeedsReview,
    IReadOnlyList<UnresolvedScopeTenant> UnscopedTenants,
    bool ReconciliationRun,
    bool MonitoredEvidenceSupplied,
    IReadOnlyList<ScopeDiscrepancy> Discrepancies,
    IReadOnlyList<ScopeException> Ambiguous,
    IReadOnlyList<ScopeException> UnscopedRecords,
    IReadOnlyList<string> Notices)
{
    /// <summary>
    /// Set when identities were resolved automatically this run.
    /// </summary>
    public AutomaticMappingSummary? AutomaticMappings { get; init; }

    public int AcronisCustomerTenants =>
        InScope.Count
        + Excluded.Count
        + NeedsReview.Count
        + UnscopedTenants.Count;
}

/// <summary>
/// Combines tenant selection and reconciliation into one report.
/// </summary>
public static class MonitoringScopeReportBuilder
{
    public static MonitoringScopeReport Build(
        DateOnly asOfDate,
        IReadOnlyList<string> entitlingItems,
        IReadOnlyList<MonitoringEntitlementObservation> observations,
        MonitoringTenantSelectionResult selection,
        ReconciliationReport? reconciliation,
        bool monitoredEvidenceSupplied,
        IReadOnlyList<TenantDto> tenants)
    {
        ArgumentNullException.ThrowIfNull(entitlingItems);
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(tenants);

        var names =
            tenants
                .Where(tenant => !string.IsNullOrWhiteSpace(tenant.Id))
                .GroupBy(
                    tenant => tenant.Id.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Name,
                    StringComparer.OrdinalIgnoreCase);

        string NameOf(string? tenantId) =>
            tenantId is not null
            && names.TryGetValue(tenantId.Trim(), out var name)
                ? name
                : tenantId ?? "<unknown tenant>";

        var inScope =
            selection.Tenants
                .Where(
                    finding =>
                        finding.Status
                        == MonitoringTenantSelectionStatus.CandidateForInventory)
                .Select(
                    finding =>
                        new InScopeTenant(
                            finding.Tenant.Name,
                            finding.Tenant.Id,
                            finding.CustomerNumber,
                            finding.EntitlingSubscriptions,
                            finding.IsOperatorTenant))
                .ToArray();

        var excluded =
            selection.ExcludedTenants
                .Select(
                    excludedTenant =>
                        new ExcludedScopeTenant(
                            excludedTenant.Tenant.Name,
                            excludedTenant.Tenant.Id,
                            ScopeExclusionReason.Configured,
                            "Excluded by configuration.",
                            null))
                .Concat(
                    selection.Tenants
                        .Where(
                            finding =>
                                finding.Status
                                == MonitoringTenantSelectionStatus.NotEntitled)
                        .Select(
                            finding =>
                                new ExcludedScopeTenant(
                                    finding.Tenant.Name,
                                    finding.Tenant.Id,
                                    ScopeExclusionReason.NotEntitled,
                                    "Not entitled. " + finding.Reason,
                                    finding.CustomerNumber)))
                .ToArray();

        UnresolvedScopeTenant Unresolved(
            MonitoringTenantSelectionFinding finding) =>
            new(
                finding.Tenant.Name,
                finding.Tenant.Id,
                finding.CustomerNumber,
                finding.Reason);

        var needsReview =
            selection.Tenants
                .Where(
                    finding =>
                        finding.Status
                        == MonitoringTenantSelectionStatus.NeedsReview)
                .Select(Unresolved)
                .ToArray();

        var unscopedTenants =
            selection.Tenants
                .Where(
                    finding =>
                        finding.Status
                        == MonitoringTenantSelectionStatus.Unclassified)
                .Select(Unresolved)
                .ToArray();

        ScopeDiscrepancy Describe(
            ReconciliationDiscrepancy discrepancy)
        {
            var result =
                ResultOf(reconciliation!, discrepancy);

            return new ScopeDiscrepancy(
                NameOf(result?.AcronisTenantId),
                result?.AcronisTenantId,
                result?.BcCustomerNo,
                discrepancy);
        }

        var discrepancies =
            (reconciliation?.WouldBeTickets ?? [])
                .Select(Describe)
                .ToArray();

        var ambiguous =
            (reconciliation?.Results ?? [])
                .Where(
                    result =>
                        result.Bucket == ReconciliationBucket.Ambiguous)
                .Select(
                    result =>
                        new ScopeException(
                            result.Bucket,
                            result.AcronisTenantId is null
                                ? result.SubjectId
                                : $"{NameOf(result.AcronisTenantId)} | " +
                                  (result.BcCustomerNo ?? "<no customer>"),
                            result.Reasons))
                .ToArray();

        // Unmapped tenants are already listed from the selection; the
        // queue adds the Business Central side only.
        var unscopedRecords =
            (reconciliation?.Results ?? [])
                .Where(
                    result =>
                        result.Bucket == ReconciliationBucket.Unscoped
                        && result.Subject
                            != ReconciliationSubject.AcronisTenant)
                .Select(
                    result =>
                        new ScopeException(
                            result.Bucket,
                            $"{result.Subject} {result.SubjectId}",
                            result.Reasons))
                .ToArray();

        var entitledCustomers =
            observations
                .Where(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.ActiveCandidate
                        && !string.IsNullOrWhiteSpace(
                            observation.CustomerNumber))
                .Select(observation => observation.CustomerNumber!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

        return new MonitoringScopeReport(
            asOfDate,
            entitlingItems,
            entitledCustomers,
            inScope,
            excluded,
            needsReview,
            unscopedTenants,
            reconciliation is not null,
            monitoredEvidenceSupplied,
            discrepancies,
            ambiguous,
            unscopedRecords,
            selection.Notices);
    }

    private static ReconciliationResult? ResultOf(
        ReconciliationReport reconciliation,
        ReconciliationDiscrepancy discrepancy) =>
        reconciliation.Results.FirstOrDefault(
            result => result.Discrepancies.Contains(discrepancy));
}
