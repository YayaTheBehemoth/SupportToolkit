using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Modules.BackupHealth.Reporting;
using SupportToolkit.Modules.BackupHealth.Services;

namespace SupportToolkit.Ui.Interactive;

public sealed partial class SupportToolkitInteractiveShell
{
    private async Task ChangeProfileAsync(
        CancellationToken cancellationToken)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        if (configuration.Profiles.Count == 0)
        {
            Console.WriteLine(
                "No SupportToolkit profiles are configured."
            );

            Console.WriteLine();

            Pause();

            return;
        }

        var profiles =
            configuration.Profiles.Keys
                .OrderBy(
                    name =>
                        name,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        Console.WriteLine(
            "Change profile"
        );

        Console.WriteLine(
            "=============="
        );

        Console.WriteLine();

        for (var index = 0;
             index < profiles.Count;
             index++)
        {
            var profileName =
                profiles[index];

            var activeMarker =
                string.Equals(
                    profileName,
                    configuration.ActiveProfile,
                    StringComparison.OrdinalIgnoreCase
                )
                    ? " *"
                    : string.Empty;

            Console.WriteLine(
                $"{index + 1}. {profileName}{activeMarker}"
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "0. Back"
        );

        Console.WriteLine();

        var choice =
            ReadMenuChoice(
                minimum:
                    0,
                maximum:
                    profiles.Count
            );

        Console.WriteLine();

        if (choice == 0)
        {
            return;
        }

        var selectedProfile =
            profiles[
                choice - 1
            ];

        configuration.ActiveProfile =
            selectedProfile;

        await _configurationStore.SaveAsync(
            configuration,
            cancellationToken
        );

        Console.WriteLine(
            $"Active profile changed to '{selectedProfile}'."
        );

        Console.WriteLine();

        Pause();
    }

    private async Task<string> GetActiveProfileNameAsync(
        CancellationToken cancellationToken)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        if (configuration.Profiles.Count == 0)
        {
            return
                "(not configured)";
        }

        if (string.IsNullOrWhiteSpace(
                configuration.ActiveProfile))
        {
            return
                "(not selected)";
        }

        return configuration.ActiveProfile;
    }
}
