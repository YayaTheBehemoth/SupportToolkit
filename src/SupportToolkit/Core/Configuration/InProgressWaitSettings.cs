namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Validated settings for waiting on in-progress resources during a
/// review. A MaxWaitSeconds of 0 disables waiting.
/// </summary>
public sealed record InProgressWaitSettings(
    int MaxWaitSeconds,
    int PollIntervalSeconds)
{
    public const int DefaultMaxWaitSeconds = 1800;

    public const int DefaultPollIntervalSeconds = 30;

    /// <summary>
    /// Upper bound that catches a value typed in the wrong unit.
    /// </summary>
    public const int MaxAllowedWaitSeconds = 24 * 60 * 60;

    public static InProgressWaitSettings Default { get; } =
        new(DefaultMaxWaitSeconds, DefaultPollIntervalSeconds);

    public static InProgressWaitSettings Create(
        int? maxWaitSeconds,
        int? pollIntervalSeconds)
    {
        var max =
            maxWaitSeconds ?? DefaultMaxWaitSeconds;

        var poll =
            pollIntervalSeconds ?? DefaultPollIntervalSeconds;

        if (max < 0 || max > MaxAllowedWaitSeconds)
        {
            throw new InvalidOperationException(
                "inProgressMaxWaitSeconds must be between 0 " +
                $"(disabled) and {MaxAllowedWaitSeconds}.");
        }

        if (max > 0 && (poll < 1 || poll > max))
        {
            throw new InvalidOperationException(
                "inProgressPollIntervalSeconds must be at least 1 " +
                "and no more than inProgressMaxWaitSeconds.");
        }

        return new InProgressWaitSettings(max, max == 0 ? 0 : poll);
    }
}
