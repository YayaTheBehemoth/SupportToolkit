namespace SupportToolkit.Modules.BackupHealth.Models;

public enum BackupExceptionSeverity
{
    Warning,
    Critical
}

public sealed class BackupException
{
    public required string TenantName { get; init; }

    public required string ResourceId { get; init; }

    public required string ResourceName { get; init; }

    public required BackupExceptionSeverity Severity { get; init; }

    public required IReadOnlyList<string> Reasons { get; init; }

    public DateTimeOffset? LastSuccessfulBackup { get; init; }
}