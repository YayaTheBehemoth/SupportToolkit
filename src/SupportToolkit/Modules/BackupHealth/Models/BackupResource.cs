namespace SupportToolkit.Modules.BackupHealth.Models;

public sealed class BackupResource
{
    public required string TenantId { get; init; }

    public required string TenantName { get; init; }

    public required string ResourceId { get; init; }

    public required string ResourceName { get; init; }

    public required string ResourceType { get; init; }

    public required string Status { get; init; }

    public DateTimeOffset? LastSuccessfulBackup { get; init; }

    public required IReadOnlyList<BackupAlert> Alerts { get; init; }
}