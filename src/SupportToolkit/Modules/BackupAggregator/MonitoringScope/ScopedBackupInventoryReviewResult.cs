
using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Result of an explicitly scoped inventory review.
///
/// Inventory completeness describes whether selected tenants were
/// successfully reviewed.
///
/// Scope completeness describes whether the selection itself is
/// sufficiently resolved.
///
/// Neither concept should be silently substituted for the other.
/// </summary>
public sealed record ScopedBackupInventoryReviewResult(
    MonitoringTenantSelectionResult Selection,
    AggregatedBackupReport Inventory)
{
    /// <summary>
    /// Full scope listing with reconciliation, when the run produced
    /// one.
    /// </summary>
    public MonitoringScopeReport? ScopeReport { get; init; }

    public bool IsInventoryComplete =>
        Inventory.IsTenantReviewComplete
        && Inventory.IsFullyAccountedFor
        && Inventory.TenantsAttempted
            == Selection.InventoryCandidates.Count;

    public bool IsScopeComplete =>
        Selection.Tenants.Count > 0
        && Selection.InventoryCandidates.Count > 0
        && Selection.NeedsReviewCount == 0
        && Selection.UnclassifiedCount == 0
        && Selection.UnmatchedEvidence.Count == 0
        && Selection.Notices.Count == 0;

    public bool IsOverallComplete =>
        IsInventoryComplete
        && IsScopeComplete;

    /// <summary>
    /// Why no ticket may be created from this review. Empty only when
    /// the review is overall complete: a ticket asserts that every
    /// monitored tenant was reviewed, so an incomplete scope or
    /// inventory must never produce one.
    /// </summary>
    public IReadOnlyList<string> TicketBlockers
    {
        get
        {
            var blockers = new List<string>();

            if (!IsInventoryComplete)
            {
                blockers.Add(
                    $"The inventory review is incomplete: " +
                    $"{Inventory.FailedTenantCount} tenant review(s) " +
                    "failed or resources are not fully accounted for.");
            }

            if (Selection.InventoryCandidates.Count == 0)
            {
                blockers.Add(
                    "No tenant is in scope, so there is nothing to " +
                    "report.");
            }

            if (Selection.NeedsReviewCount > 0)
            {
                blockers.Add(
                    $"{Selection.NeedsReviewCount} tenant(s) need scope " +
                    "review.");
            }

            if (Selection.UnclassifiedCount > 0)
            {
                blockers.Add(
                    $"{Selection.UnclassifiedCount} tenant(s) could not " +
                    "be matched to a Business Central customer.");
            }

            if (Selection.UnmatchedEvidence.Count > 0)
            {
                blockers.Add(
                    $"{Selection.UnmatchedEvidence.Count} entitlement " +
                    "record(s) have no Acronis tenant.");
            }

            foreach (var notice in Selection.Notices)
            {
                blockers.Add(notice);
            }

            return blockers;
        }
    }

    /// <summary>
    /// One line for the ticket saying what the review covered.
    /// </summary>
    public string TicketScopeNote
    {
        get
        {
            var inScope =
                ScopeReport?.InScope
                ?? [];

            var customers =
                inScope.Count(tenant => !tenant.IsOperatorTenant);

            var includesOperator =
                inScope.Any(tenant => tenant.IsOperatorTenant);

            return
                $"Monitored tenants only: {customers} entitled customer " +
                $"tenant(s) by Business Central entitlement" +
                (includesOperator
                    ? ", plus the operator's own tenant."
                    : ".");
        }
    }
}
