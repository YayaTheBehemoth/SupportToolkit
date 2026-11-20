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

    /// <summary>
    /// The status operators see, when the backup system's adapter states
    /// it. Unset, it is derived from ResourceState and LastResult, which
    /// carry Acronis's own state names.
    /// </summary>
    public string? DisplayStatus { get; init; }

    /// <summary>
    /// Why an Unknown entry could not be classified, when the adapter
    /// knows. Unset, it is derived like DisplayStatus.
    /// </summary>
    public string? UnclassifiedReason { get; init; }

    public bool IsInProgress =>
        BackupResourceStates.IsInProgress(ResourceState);
}
