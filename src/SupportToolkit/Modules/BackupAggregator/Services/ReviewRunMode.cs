namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// How a review was started, which decides whether it waits for
/// in-progress resources.
///
/// A person who starts a review and creates the ticket themselves wants
/// the result quickly and can judge a mid-backup snapshot; an unattended
/// run must not create tickets from one, because a repeat run could then
/// describe the same resources differently.
/// </summary>
public enum ReviewRunMode
{
    /// <summary>
    /// An operator started the review. One pass, no waiting.
    /// </summary>
    Manual,

    /// <summary>
    /// An unattended run. Waits for in-progress resources using the
    /// profile's review settings.
    /// </summary>
    Automatic
}
