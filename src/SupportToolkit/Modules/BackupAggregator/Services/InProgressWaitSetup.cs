using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Builds the in-progress wait for a production review from the run
/// mode and the active profile.
///
/// A manual run never waits and does not read the profile's wait
/// settings. An automatic run waits as the profile says.
///
/// Where its progress messages go is not decided here: the host that
/// constructs the workflow supplies the sink, so a console, a web page or
/// a test can each receive them.
/// </summary>
public static class InProgressWaitSetup
{
    public static async Task<InProgressWaitOptions> LoadAsync(
        SupportToolkitConfigurationResolver resolver,
        string moduleCommand,
        ReviewRunMode runMode,
        CancellationToken cancellationToken)
    {
        if (runMode == ReviewRunMode.Manual)
        {
            return InProgressWaitOptions.Disabled;
        }

        var settings =
            await resolver.GetInProgressWaitSettingsAsync(
                moduleCommand,
                cancellationToken);

        return InProgressWaitOptions.Create(
            TimeSpan.FromSeconds(settings.MaxWaitSeconds),
            TimeSpan.FromSeconds(settings.PollIntervalSeconds));
    }
}
