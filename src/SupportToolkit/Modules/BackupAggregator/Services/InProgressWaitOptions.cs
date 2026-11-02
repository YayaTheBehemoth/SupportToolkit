namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// How long a review waits for in-progress resources to finish, and
/// how often it checks again. A MaxWait of zero disables waiting.
/// </summary>
public sealed record InProgressWaitOptions(
    TimeSpan MaxWait,
    TimeSpan PollInterval)
{
    public static InProgressWaitOptions Disabled { get; } =
        new(TimeSpan.Zero, TimeSpan.Zero);

    public bool Enabled =>
        MaxWait > TimeSpan.Zero;

    /// <summary>
    /// Validates the values; a zero MaxWait disables waiting and
    /// needs no interval.
    /// </summary>
    public static InProgressWaitOptions Create(
        TimeSpan maxWait,
        TimeSpan pollInterval)
    {
        if (maxWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxWait),
                "The maximum wait cannot be negative.");
        }

        if (maxWait == TimeSpan.Zero)
        {
            return Disabled;
        }

        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "The poll interval must be positive when waiting is " +
                "enabled.");
        }

        return new InProgressWaitOptions(maxWait, pollInterval);
    }
}
