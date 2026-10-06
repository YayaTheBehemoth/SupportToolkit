namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Consolidated result for one complete backup-report aggregation run.
/// </summary>
public sealed class AggregatedBackupReport
{
    public required IReadOnlyList<TenantBackupReport> Tenants { get; init; }

    public int TenantCount =>
        Tenants.Count;

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
}