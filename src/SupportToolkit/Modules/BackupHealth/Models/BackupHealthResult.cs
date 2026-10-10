namespace SupportToolkit.Modules.BackupHealth.Models;

/// <summary>
/// Represents the complete result of one BackupHealth evaluation.
///
/// The workflow owns data retrieval and exception evaluation.
/// Presentation layers consume this result without needing to know how
/// BackupHealth obtained or evaluated its data.
/// </summary>
public sealed class BackupHealthResult
{
    public required BackupHealthSnapshot Snapshot { get; init; }

    public required IReadOnlyList<BackupException> Exceptions { get; init; }
}