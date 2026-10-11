
namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Coordinates first-run configuration and interactive startup.
///
/// The caller supplies the configuration wizard and interactive shell.
/// This keeps startup decisions independent of console interaction.
/// </summary>
public sealed class FirstRunStartup
{
    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    public FirstRunStartup(
        ISupportToolkitConfigurationStore configurationStore)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );
    }

    public async Task<int> RunAsync(
        Func<Task<int>> runConfiguration,
        Func<Task<int>> runInteractive)
    {
        ArgumentNullException.ThrowIfNull(
            runConfiguration
        );

        ArgumentNullException.ThrowIfNull(
            runInteractive
        );

        var configuration =
            await _configurationStore.LoadAsync();

        if (configuration.Profiles.Count == 0)
        {
            Console.WriteLine(
                "Welcome to SupportToolkit."
            );

            Console.WriteLine();

            Console.WriteLine(
                "No configuration profile was found."
            );

            Console.WriteLine(
                "Starting first-time configuration."
            );

            Console.WriteLine();

            var setupExitCode =
                await runConfiguration();

            if (setupExitCode != 0)
            {
                return setupExitCode;
            }

            var updatedConfiguration =
                await _configurationStore.LoadAsync();

            if (updatedConfiguration.Profiles.Count == 0)
            {
                Console.WriteLine();

                Console.WriteLine(
                    "No configuration was saved."
                );

                return 0;
            }

            Console.WriteLine();

            Console.WriteLine(
                "Configuration complete."
            );

            Console.WriteLine(
                "Starting SupportToolkit..."
            );

            Console.WriteLine();
        }

        return await runInteractive();
    }
}
