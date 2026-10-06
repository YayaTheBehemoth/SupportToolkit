namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Consolidated backup-reporting result for one tenant.
/// </summary>
public sealed class TenantBackupReport
{
    public required string TenantName { get; init; }

    public required IReadOnlyList<BackupReportEntry> Entries { get; init; }

    public int RowsChecked =>
        Entries.Count;

    public int HealthyRowsSuppressed =>
        Entries.Count(
            entry =>
                entry.Classification
                == BackupReportEntryClassification.Healthy
        );

    public int FindingsCount =>
        Entries.Count(
            entry =>
                entry.Classification
                == BackupReportEntryClassification.NeedsReview
        );

    public int UnknownRowsCount =>
        Entries.Count(
            entry =>
                entry.Classification
                == BackupReportEntryClassification.Unknown
        );

    public bool RequiresReview =>
        FindingsCount > 0
        || UnknownRowsCount > 0;

    public IReadOnlyList<BackupReportEntry> Findings =>
        Entries
            .Where(
                entry =>
                    entry.Classification
                    == BackupReportEntryClassification.NeedsReview
            )
            .ToList()
            .AsReadOnly();

    public IReadOnlyList<BackupReportEntry> UnknownRows =>
        Entries
            .Where(
                entry =>
                    entry.Classification
                    == BackupReportEntryClassification.Unknown
            )
            .ToList()
            .AsReadOnly();
}