namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    /// <summary>
    /// How long a review waits for in-progress resources, and how
    /// often it checks. Defaults apply when no profile or setting
    /// exists, so a run without profiles still behaves the same.
    /// Invalid values are an error, not a silent fallback.
    /// </summary>
    public async Task<InProgressWaitSettings> GetInProgressWaitSettingsAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var configuration =
            await _configurationStore.LoadAsync(cancellationToken);

        if (configuration.Profiles.Count == 0
            || string.IsNullOrWhiteSpace(configuration.ActiveProfile))
        {
            return InProgressWaitSettings.Default;
        }

        var settings =
            GetRequiredActiveProfile(configuration)
                .Profile.Review
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            moduleCommand,
                            StringComparison.OrdinalIgnoreCase))
                .Value;

        return InProgressWaitSettings.Create(
            settings?.InProgressMaxWaitSeconds,
            settings?.InProgressPollIntervalSeconds);
    }
}
