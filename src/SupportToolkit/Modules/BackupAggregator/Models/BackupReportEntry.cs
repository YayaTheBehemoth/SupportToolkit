namespace SupportToolkit.Modules.BackupAggregator.Models;

public sealed class BackupReportEntry
{
    public required string DeviceName { get; init; }

    public required string LastResult { get; init; }

    public required BackupReportEntryClassification Classification
    {
        get;
        init;
    }

    public DateTimeOffset? LastBackupRun { get; init; }

    public DateTimeOffset? LastSuccessfulBackup { get; init; }

    public string? PlanName { get; init; }

    public string? DeviceState { get; init; }
}