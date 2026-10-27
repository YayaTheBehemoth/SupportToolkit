
using SupportToolkit.Cli.Commands;
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

/// <summary>
/// Human-oriented interactive console interface for SupportToolkit.
///
/// This class owns navigation, prompts, progress messages, and presentation.
/// Operational behavior remains in module workflows.
///
/// The raw CLI remains available independently for development, scripting,
/// and automation.
/// </summary>
public sealed partial class SupportToolkitInteractiveShell
{
    private const string DebugEnvironmentVariable =
        "SUPPORTTOOLKIT_DEBUG";

    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly ConfigureCommand _configureCommand;
    private readonly BackupAggregatorWorkflow _backupAggregatorWorkflow;
    private readonly BackupHealthWorkflow _backupHealthWorkflow;

    private readonly BackupAggregatorScopedWorkflow?
        _backupAggregatorScopedWorkflow;

    private readonly ConsoleBackupAggregatorReporter
        _backupAggregatorReporter;

    private readonly ConsoleBackupHealthReporter
        _backupHealthReporter;

    private readonly ConsoleTicketDraftPreviewer
        _ticketDraftPreviewer;

    public SupportToolkitInteractiveShell(
        ISupportToolkitConfigurationStore configurationStore,
        ConfigureCommand configureCommand,
        BackupAggregatorWorkflow backupAggregatorWorkflow,
        BackupHealthWorkflow backupHealthWorkflow,
        BackupAggregatorScopedWorkflow? backupAggregatorScopedWorkflow = null)
    {
        _backupAggregatorScopedWorkflow =
            backupAggregatorScopedWorkflow;

        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore));

        _configureCommand =
            configureCommand
            ?? throw new ArgumentNullException(
                nameof(configureCommand));

        _backupAggregatorWorkflow =
            backupAggregatorWorkflow
            ?? throw new ArgumentNullException(
                nameof(backupAggregatorWorkflow));

        _backupHealthWorkflow =
            backupHealthWorkflow
            ?? throw new ArgumentNullException(
                nameof(backupHealthWorkflow));

        _backupAggregatorReporter =
            new ConsoleBackupAggregatorReporter();

        _backupHealthReporter =
            new ConsoleBackupHealthReporter();

        _ticketDraftPreviewer =
            new ConsoleTicketDraftPreviewer();
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
                    cancellationToken);

            var debugEnabled =
                IsDebugEnabled();

            WriteMainMenu(
                activeProfile,
                debugEnabled);

            var choice =
                ReadMenuChoice(
                    minimum: 1,
                    maximum: 6);

            Console.WriteLine();

            switch (choice)
            {
                case 1:
                    await RunBackupAggregatorMenuAsync(
                        cancellationToken);
                    break;

                case 2:
                    await RunBackupHealthMenuAsync(
                        cancellationToken);
                    break;

                case 3:
                    await ChangeProfileAsync(
                        cancellationToken);
                    break;

                case 4:
                    await RunConfigurationWizardAsync();
                    break;

                case 5:
                    ToggleDebugMode();
                    break;

                case 6:
                    Console.WriteLine(
                        "Shutting down. All session data has been " +
                        "wiped: sign-ins, reviews and ticket drafts " +
                        "are not kept, so you will have to log in " +
                        "again and redo everything next time.");
                    return 0;
            }
        }
    }

    private async Task RunConfigurationWizardAsync()
    {
        try
        {
            await _configureCommand.RunAsync(
                Array.Empty<string>());
        }
        catch (Exception exception)
        {
            WriteError(exception);
        }

        Console.WriteLine();

        Pause();
    }

    private static void ToggleDebugMode()
    {
        var enableDebug =
            !IsDebugEnabled();

        Environment.SetEnvironmentVariable(
            DebugEnvironmentVariable,
            enableDebug ? "true" : null);

        Console.WriteLine(
            enableDebug
                ? "Debug logging enabled for this SupportToolkit session."
                : "Debug logging disabled for this SupportToolkit session.");

        Console.WriteLine();

        Pause();
    }

    private static bool IsDebugEnabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable(
                DebugEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteCreatedTicket(
        CreatedTicket ticket)
    {
        ConsoleCreatedTicketReporter.Write(
            Console.Out,
            ticket);
    }

    private static void WriteProgress(string message)
    {
        Console.WriteLine(message);
    }

    private static void WriteError(Exception exception)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            $"ERROR: {ConsoleErrorFormatter.Format(exception)}");

        Console.Error.WriteLine();
    }

    private static void WriteMainMenu(
        string activeProfile,
        bool debugEnabled)
    {
        Console.WriteLine();

        Console.WriteLine("SupportToolkit");
        Console.WriteLine("===============");
        Console.WriteLine();

        Console.WriteLine(
            $"Active profile: {activeProfile}");

        Console.WriteLine(
            $"Debug logging:  " +
            $"{(debugEnabled ? "enabled" : "disabled")}");

        Console.WriteLine();

        Console.WriteLine("1. Backup Aggregator");
        Console.WriteLine("2. Backup Health");
        Console.WriteLine("3. Change profile");
        Console.WriteLine("4. Configuration wizard");

        Console.WriteLine(
            debugEnabled
                ? "5. Disable debug logging"
                : "5. Enable debug logging");

        Console.WriteLine("6. Exit");
        Console.WriteLine();
    }

    private static int ReadMenuChoice(
        int minimum,
        int maximum)
    {
        while (true)
        {
            Console.Write("> ");

            var input =
                Console.ReadLine();

            if (int.TryParse(input, out var choice)
                && choice >= minimum
                && choice <= maximum)
            {
                return choice;
            }

            Console.WriteLine(
                $"Enter a number from {minimum} to {maximum}.");
        }
    }

    private static bool ReadConfirmation(
        string prompt,
        bool defaultValue)
    {
        while (true)
        {
            Console.Write(prompt);

            var value =
                Console.ReadLine();

            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (value.Equals(
                    "y",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "yes",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals(
                    "n",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "no",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine("Expected 'y' or 'n'.");
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
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
        Console.WriteLine();
    }

    private static void EnsureInteractiveConsole()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "The interactive SupportToolkit shell requires " +
                "an interactive console.");
        }
    }
}
