namespace SupportToolkit.Modules.BackupHealth.Models;


/// Identifies the severity assigned to a backup exception in the SupportToolkit domain model.

public enum BackupExceptionSeverity
{
    Warning,
    Critical
}


/// Represents a normalized backup issue discovered for a tenant resource.
/// This model is distinct from raw Acronis DTOs and captures the user-facing exception result
/// produced by BackupHealth evaluation.

public sealed class BackupException
{
    public required string TenantName { get; init; }

    public required string ResourceId { get; init; }

    public required string ResourceName { get; init; }

    public required BackupExceptionSeverity Severity { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public DateTimeOffset? LastSuccessfulBackup { get; init; }
}