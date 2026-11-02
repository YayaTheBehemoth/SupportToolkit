
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
    /// Why no ticket may be created from this review.
    ///
    /// Tenants that could not be matched to a customer do not block:
    /// they are not reviewed, so the ticket must say so itself (see
    /// TicketScopeWarning). Every other gap, an unresolved scope, a
    /// failed tenant review or a notice, still blocks, because a
    /// ticket must never claim coverage it does not have.
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
    /// Banner for a ticket drafted while tenants are unmatched. Null
    /// when every tenant was decided.
    /// </summary>
    public string? TicketScopeWarning =>
        Selection.UnclassifiedCount == 0
            ? null
            : $"INCOMPLETE SCOPE: {Selection.UnclassifiedCount} " +
              "tenant(s) could not be matched to a Business Central " +
              "customer and were NOT reviewed. This ticket does not " +
              "cover them; they are listed under STRAY TENANT CHECK.";

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

    /// <summary>
    /// The tenants this review covered, one line each, sorted by name
    /// then tenant id. The operator's own tenant is marked.
    /// </summary>
    public IReadOnlyList<string> TicketScopeTenantLines =>
        (ScopeReport?.InScope ?? [])
            .OrderBy(
                tenant => tenant.TenantName,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                tenant => tenant.TenantId,
                StringComparer.OrdinalIgnoreCase)
            .Select(
                tenant => tenant.IsOperatorTenant
                    ? $"{tenant.TenantName} (operator's own tenant)"
                    : tenant.TenantName)
            .ToArray();

    /// <summary>
    /// The stray tenant check as ticket lines. Informational: it never
    /// blocks a ticket. Null when the run produced no scope report.
    /// An unavailable leak check is stated, never left out.
    /// </summary>
    public IReadOnlyList<string>? TicketStrayLines
    {
        get
        {
            var check = ScopeReport?.StrayTenants;

            if (check is null)
            {
                return null;
            }

            var lines = new List<string>();

            if (!check.LeakCheckAvailable)
            {
                lines.Add(
                    "Leak check unavailable: no service items are " +
                    "configured.");
            }
            else if (check.LeakCandidates.Count == 0)
            {
                lines.Add("Leak candidates: none.");
            }
            else
            {
                lines.Add(
                    $"Leak candidates ({check.LeakCandidates.Count}): " +
                    "matched customer holds neither the backup nor " +
                    "the monitoring item.");

                lines.AddRange(
                    check.LeakCandidates.Select(
                        tenant =>
                            $"- {tenant.TenantName} " +
                            $"(customer {tenant.CustomerNumber})"));
            }

            if (check.Unmatched.Count == 0)
            {
                lines.Add("Unmatched tenants: none.");
            }
            else
            {
                lines.Add(
                    $"Unmatched tenants ({check.Unmatched.Count}): not " +
                    "matched to any Business Central customer; stray " +
                    "tenant or matching miss.");

                lines.AddRange(
                    check.Unmatched.Select(
                        tenant => $"- {tenant.TenantName}"));
            }

            return lines;
        }
    }
}
