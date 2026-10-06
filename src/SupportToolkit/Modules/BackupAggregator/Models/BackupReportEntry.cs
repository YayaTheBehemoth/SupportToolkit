namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Represents one workload row from the backup-reporting datasource.
///
/// The model intentionally reflects only operationally useful information.
/// It does not depend on a particular Acronis API or report transport.
/// </summary>
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