namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Represents one tenant that could not be reviewed completely.
///
/// Failed tenants are kept separate from successfully reviewed tenant reports
/// so a partial multi-tenant run can never be mistaken for complete coverage.
/// </summary>
public sealed class TenantBackupReviewFailure
{
    public required string TenantName { get; init; }

    public required string Stage { get; init; }

    public required string Reason { get; init; }
}