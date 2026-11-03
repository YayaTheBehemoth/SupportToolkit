namespace SupportToolkit.Modules.BackupAggregator.Models;

/// <summary>
/// What the wait for in-progress resources did. Absent when nothing
/// was in progress or waiting is disabled.
///
/// Pending resources are still running and are reported as in
/// progress, never as healthy. A tenant whose re-poll failed is a
/// failed tenant review, like any other.
/// </summary>
public sealed record InProgressWaitSummary(
    TimeSpan MaxWait,
    TimeSpan Elapsed,
    int Polls,
    int PendingResources,
    int PendingTenants,
    bool TimedOut);
