using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Ui.Interactive;

/// <summary>
/// Human-oriented interactive console interface for SupportToolkit.
///
/// This class owns navigation, prompts, and presentation decisions only.
/// Operational behavior remains in module workflows.
///
/// The raw CLI remains available independently for development, scripting,
/// and automation.
/// </summary>
public sealed class SupportToolkitInteractiveShell
{
    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    private readonly BackupAggregatorWorkflow
        _backupAggregatorWorkflow;

    private readonly ConsoleBackupAggregatorReporter
        _backupAggregatorReporter;

    public SupportToolkitInteractiveShell(
        ISupportToolkitConfigurationStore configurationStore,
        SupportToolkitConfigurationResolver configurationResolver,
        BackupAggregatorWorkflow backupAggregatorWorkflow)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );

        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver)
            );

        _backupAggregatorWorkflow =
            backupAggregatorWorkflow
            ?? throw new ArgumentNullException(
                nameof(backupAggregatorWorkflow)
            );

        _backupAggregatorReporter =
            new ConsoleBackupAggregatorReporter();
    }

    public async Task<int> RunAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureInteractiveConsole();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var activeProfile =
                await GetActiveProfileNameAsync(
                    cancellationToken
                );

            WriteMainMenu(
                activeProfile
            );

            var choice =
                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        3
                );

            Console.WriteLine();

            switch (choice)
            {
                case 1:
                    await RunBackupAggregatorMenuAsync(
                        cancellationToken
                    );

                    break;

                case 2:
                    await ChangeProfileAsync(
                        cancellationToken
                    );

                    break;

                case 3:
                    Console.WriteLine(
                        "Goodbye."
                    );

                    return 0;
            }
        }
    }

    private async Task RunBackupAggregatorMenuAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode =
                await _configurationResolver
                    .GetModuleModeAsync(
                        BackupAggregatorModule.ModuleCommand,
                        cancellationToken
                    );

            Console.WriteLine(
                "Backup Aggregator"
            );

            Console.WriteLine(
                "================="
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Mode: {FormatMode(mode)}"
            );

            Console.WriteLine();

            Console.WriteLine(
                "1. Review all tenants"
            );

            Console.WriteLine(
                "2. Review one tenant"
            );

            Console.WriteLine(
                "3. Back"
            );

            Console.WriteLine();

            var choice =
                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        3
                );

            Console.WriteLine();

            switch (choice)
            {
                case 1:
                    await ExecuteBackupReviewAsync(
                        () =>
                            _backupAggregatorWorkflow
                                .ReviewAllTenantsAsync(
                                    cancellationToken
                                )
                    );

                    break;

                case 2:
                    await ReviewOneTenantAsync(
                        cancellationToken
                    );

                    break;

                case 3:
                    return;
            }
        }
    }

    private async Task ReviewOneTenantAsync(
        CancellationToken cancellationToken)
    {
        Console.Write(
            "Tenant name: "
        );

        var tenantName =
            Console.ReadLine();

        if (string.IsNullOrWhiteSpace(
                tenantName))
        {
            Console.WriteLine();

            Console.WriteLine(
                "Tenant review cancelled."
            );

            Console.WriteLine();

            return;
        }

        Console.WriteLine();

        await ExecuteBackupReviewAsync(
            () =>
                _backupAggregatorWorkflow
                    .ReviewTenantAsync(
                        tenantName,
                        cancellationToken
                    )
        );
    }

    private async Task ExecuteBackupReviewAsync(
        Func<Task<AggregatedBackupReport>> review)
    {
        try
        {
            var report =
                await review();

            _backupAggregatorReporter
                .Write(
                    report
                );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"ERROR: {ConsoleErrorFormatter.Format(exception)}"
            );

            Console.Error.WriteLine();
        }

        Pause();
    }

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

    private static void WriteMainMenu(
        string activeProfile)
    {
        Console.WriteLine();

        Console.WriteLine(
            "SupportToolkit"
        );

        Console.WriteLine(
            "=============="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Active profile: {activeProfile}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "1. Backup Aggregator"
        );

        Console.WriteLine(
            "2. Change profile"
        );

        Console.WriteLine(
            "3. Exit"
        );

        Console.WriteLine();
    }

    private static int ReadMenuChoice(
        int minimum,
        int maximum)
    {
        while (true)
        {
            Console.Write(
                "> "
            );

            var input =
                Console.ReadLine();

            if (int.TryParse(
                    input,
                    out var choice)
                && choice >= minimum
                && choice <= maximum)
            {
                return choice;
            }

            Console.WriteLine(
                $"Enter a number from {minimum} to {maximum}."
            );
        }
    }

    private static string FormatMode(
        SupportToolkitMode mode)
    {
        return mode
            .ToString()
            .ToLowerInvariant();
    }

    private static void Pause()
    {
        Console.WriteLine(
            "Press Enter to continue..."
        );

        Console.ReadLine();

        Console.WriteLine();
    }

    private static void EnsureInteractiveConsole()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "The interactive SupportToolkit shell requires an " +
                "interactive console."
            );
        }
    }
}