using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Builds the in-progress wait for a production review from the
/// active profile and gives it a console progress sink.
/// </summary>
internal static class InProgressWaitSetup
{
    private static readonly object ConsoleLock = new();

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

    /// <summary>
    /// Operator-facing telemetry. Reviews run tenants concurrently, so
    /// writes are serialized.
    /// </summary>
    public static void WriteProgress(
        string message)
    {
        lock (ConsoleLock)
        {
            Console.WriteLine(message);
        }
    }
}
