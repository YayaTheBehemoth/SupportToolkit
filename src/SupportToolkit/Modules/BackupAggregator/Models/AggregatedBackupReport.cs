namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Consolidated result for one complete backup-report aggregation run.
///
/// Successfully reviewed tenants and failed tenant reviews are represented
/// separately so partial execution remains visible.
/// </summary>
public sealed class AggregatedBackupReport
{
    public required IReadOnlyList<TenantBackupReport> Tenants { get; init; }

    public IReadOnlyList<TenantBackupReviewFailure> Failures { get; init; } =
        [];

    public int TenantCount =>
        Tenants.Count;

    public int FailedTenantCount =>
        Failures.Count;

    public int TenantsAttempted =>
        TenantCount
        + FailedTenantCount;

    public int TenantsRequiringReview =>
        Tenants.Count(
            tenant =>
                tenant.RequiresReview
        );

    public int RowsChecked =>
        Tenants.Sum(
            tenant =>
                tenant.RowsChecked
        );

    public int HealthyRowsSuppressed =>
        Tenants.Sum(
            tenant =>
                tenant.HealthyRowsSuppressed
        );

    public int FindingsCount =>
        Tenants.Sum(
            tenant =>
                tenant.FindingsCount
        );

    public int UnknownRowsCount =>
        Tenants.Sum(
            tenant =>
                tenant.UnknownRowsCount
        );

    public int AccountedRows =>
        HealthyRowsSuppressed
        + FindingsCount
        + UnknownRowsCount;

    public bool IsFullyAccountedFor =>
        RowsChecked
        == AccountedRows;

    public bool IsTenantReviewComplete =>
        FailedTenantCount == 0;

    /// <summary>
    /// Resources still running when the review ended.
    /// </summary>
    public int InProgressCount =>
        Tenants.Sum(
            tenant =>
                tenant.InProgressCount
        );

    public int InProgressTenantCount =>
        Tenants.Count(
            tenant =>
                tenant.InProgressCount > 0
        );

    /// <summary>
    /// Set when the review waited for in-progress resources.
    /// </summary>
    public InProgressWaitSummary? InProgressWait { get; init; }

    /// <summary>
    /// Resources whose result is not final. After a wait this is what
    /// the wait left pending; without one, whatever was running when
    /// the review took its snapshot.
    /// </summary>
    public int InProgressPending =>
        InProgressWait?.PendingResources
        ?? InProgressCount;

    public int InProgressPendingTenants =>
        InProgressWait?.PendingTenants
        ?? InProgressTenantCount;
}