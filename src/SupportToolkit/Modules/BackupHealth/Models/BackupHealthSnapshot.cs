using System.Collections;

namespace SupportToolkit.Modules.BackupHealth.Models;

/// <summary>
/// Represents one normalized BackupHealth data load, including both usable
/// resources and any data-quality issues encountered during normalization.
///
/// The snapshot is also exposed as a read-only list of resources so callers can
/// treat it like the underlying collection when they only need the resource set.
/// </summary>
public sealed class BackupHealthSnapshot : IReadOnlyList<BackupResource>
{
    public required IReadOnlyList<BackupResource> Resources { get; init; }

    public required IReadOnlyList<BackupHealthDiagnostic> Diagnostics { get; init; }

    public int Count => Resources.Count;

    public BackupResource this[int index] => Resources[index];

    public IEnumerator<BackupResource> GetEnumerator()
    {
        return Resources.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}