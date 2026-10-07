namespace SupportToolkit.Modules.BackupAggregator.Models;

public sealed class LatestBackupActivity
{
    public required string ResourceKey { get; init; }

    public required string ResourceName { get; init; }

    public required string State { get; init; }

    public string? ResultCode { get; init; }

    public string? PolicyName { get; init; }

    public string? ResourceKind { get; init; }

    public string? ResourceSubtype { get; init; }

    public required DateTimeOffset ActivityTimestamp { get; init; }

    public string? ActivityId { get; init; }

    public string? TaskId { get; init; }
}