
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
}
