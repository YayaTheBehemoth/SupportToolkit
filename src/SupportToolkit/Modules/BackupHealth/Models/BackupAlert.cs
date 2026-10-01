namespace SupportToolkit.Modules.BackupHealth.Models;

public sealed class BackupAlert
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Category { get; init; }

    public required string Severity { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}