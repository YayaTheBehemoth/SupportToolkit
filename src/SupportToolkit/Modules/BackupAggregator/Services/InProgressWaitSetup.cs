using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Builds the in-progress wait for a production review from the
/// active profile.
///
/// Where its progress messages go is not decided here: the host that
/// constructs the workflow supplies the sink, so a console, a web page or
/// a test can each receive them.
/// </summary>
internal static class InProgressWaitSetup
{
    public static async Task<InProgressWaitOptions> LoadAsync(
        SupportToolkitConfigurationResolver resolver,
        string moduleCommand,
        CancellationToken cancellationToken)
    {
        var settings =
            await resolver.GetInProgressWaitSettingsAsync(
                moduleCommand,
                cancellationToken);

        return InProgressWaitOptions.Create(
            TimeSpan.FromSeconds(settings.MaxWaitSeconds),
            TimeSpan.FromSeconds(settings.PollIntervalSeconds));
    }
}
