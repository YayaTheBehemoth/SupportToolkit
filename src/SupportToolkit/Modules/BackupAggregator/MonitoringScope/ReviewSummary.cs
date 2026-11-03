namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// The headline numbers of a scoped review, as data. Any user interface
/// formats them as it likes; nothing here is display text.
/// </summary>
public sealed record ReviewSummary(
    int TenantCount,
    int TenantsAttempted,
    int FindingsCount,
    int UnclassifiedCount,
    bool IsComplete)
{
    public static ReviewSummary From(
        ScopedBackupInventoryReviewResult review)
    {
        ArgumentNullException.ThrowIfNull(review);

        return new ReviewSummary(
            review.Inventory.TenantCount,
            review.Inventory.TenantsAttempted,
            review.Inventory.FindingsCount,
            review.Inventory.UnknownRowsCount,
            review.IsOverallComplete);
    }
}
