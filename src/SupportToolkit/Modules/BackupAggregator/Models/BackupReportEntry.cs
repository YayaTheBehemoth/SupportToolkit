namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Represents one normalized backup resource from any inventory domain.
/// </summary>
public sealed class BackupReportEntry
{
    public required string ResourceName { get; init; }

    public required string LastResult { get; init; }

    public required BackupReportEntryClassification Classification
    {
        get;
        init;
    }

    public DateTimeOffset? LastBackupRun { get; init; }

    public DateTimeOffset? LastSuccessfulBackup { get; init; }

    public string? PlanName { get; init; }

    public string? ResourceState { get; init; }
}
