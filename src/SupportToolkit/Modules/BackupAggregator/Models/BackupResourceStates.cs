namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// Raw Acronis resource states shared by classification, display and
/// the in-progress wait, so they cannot disagree about what "running"
/// means.
/// </summary>
public static class BackupResourceStates
{
    /// <summary>
    /// A backup or Microsoft 365 task that has not finished yet.
    /// </summary>
    public static bool IsInProgress(
        string? state)
    {
        return string.Equals(
                   state,
                   "running",
                   StringComparison.OrdinalIgnoreCase
               )
               || string.Equals(
                   state,
                   "backup",
                   StringComparison.OrdinalIgnoreCase
               );
    }
}
