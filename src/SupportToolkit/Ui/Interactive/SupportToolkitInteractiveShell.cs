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
    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly BackupAggregatorWorkflow
        _backupAggregatorWorkflow;

    private readonly BackupHealthWorkflow
        _backupHealthWorkflow;

    private readonly ConsoleBackupAggregatorReporter
        _backupAggregatorReporter;

    private readonly ConsoleBackupHealthReporter
        _backupHealthReporter;

    private readonly ConsoleTicketDraftPreviewer
        _ticketDraftPreviewer;

    public SupportToolkitInteractiveShell(
        ISupportToolkitConfigurationStore configurationStore,
        BackupAggregatorWorkflow backupAggregatorWorkflow,
        BackupHealthWorkflow backupHealthWorkflow)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );

        _backupAggregatorWorkflow =
            backupAggregatorWorkflow
            ?? throw new ArgumentNullException(
                nameof(backupAggregatorWorkflow)
            );

        _backupHealthWorkflow =
            backupHealthWorkflow
            ?? throw new ArgumentNullException(
                nameof(backupHealthWorkflow)
            );

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
                        4
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
                    await RunBackupHealthMenuAsync(
                        cancellationToken
                    );

                    break;

                case 3:
                    await ChangeProfileAsync(
                        cancellationToken
                    );

                    break;

                case 4:
                    Console.WriteLine(
                        "Goodbye."
                    );

                    return 0;
            }
        }
    }

    private static string FormatReviewState(
        AggregatedBackupReport? review)
    {
        if (review is null)
        {
            return "not run";
        }

        return
            $"{review.TenantCount} tenant(s), " +
            $"{review.FindingsCount} finding(s)";
    }

    private static void WriteFullReviewRequired()
    {
        Console.WriteLine(
            "A full tenant review is required first."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Run 'Review all tenants' before previewing or submitting a ticket."
        );

        Console.WriteLine();
    }

    private static void WriteCreatedTicket(
        CreatedTicket ticket)
    {
        Console.WriteLine(
            "Ticket request completed successfully."
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Ticket ID: {ticket.Id}"
        );

        if (!string.IsNullOrWhiteSpace(
                ticket.Url))
        {
            Console.WriteLine(
                $"API URL:   {ticket.Url}"
            );
        }

        Console.WriteLine();
    }

    private static void WriteProgress(
        string message)
    {
        Console.WriteLine(
            message
        );
    }

    private static void WriteError(
        Exception exception)
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            $"ERROR: {ConsoleErrorFormatter.Format(exception)}"
        );

        Console.Error.WriteLine();
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
            "2. Backup Health"
        );

        Console.WriteLine(
            "3. Change profile"
        );

        Console.WriteLine(
            "4. Exit"
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

    private static bool ReadConfirmation(
        string prompt,
        bool defaultValue)
    {
        while (true)
        {
            Console.Write(
                prompt
            );

            var value =
                Console.ReadLine();

            if (string.IsNullOrWhiteSpace(
                    value))
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

            Console.WriteLine(
                "Expected 'y' or 'n'."
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