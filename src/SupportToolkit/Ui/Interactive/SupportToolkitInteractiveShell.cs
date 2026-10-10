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
public sealed class SupportToolkitInteractiveShell
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

    private async Task RunBackupAggregatorMenuAsync(
        CancellationToken cancellationToken)
    {
        AggregatedBackupReport? currentFullReview =
            null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode =
                await _backupAggregatorWorkflow
                    .GetModeAsync(
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
                $"Mode:        {FormatMode(mode)}"
            );

            Console.WriteLine(
                $"Full review: {FormatReviewState(currentFullReview)}"
            );

            Console.WriteLine();

            Console.WriteLine(
                currentFullReview is null
                    ? "1. Review all tenants"
                    : "1. Refresh full review"
            );

            Console.WriteLine(
                "2. Review one tenant"
            );

            Console.WriteLine(
                "3. Preview ticket"
            );

            Console.WriteLine(
                "4. Submit ticket"
            );

            Console.WriteLine(
                "5. Back"
            );

            Console.WriteLine();

            var choice =
                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        5
                );

            Console.WriteLine();

            switch (choice)
            {
                case 1:
                    currentFullReview =
                        await ReviewAllTenantsAsync(
                            cancellationToken
                        );

                    break;

                case 2:
                    await ReviewOneTenantAsync(
                        cancellationToken
                    );

                    break;

                case 3:
                    PreviewTicket(
                        currentFullReview
                    );

                    break;

                case 4:
                    await SubmitTicketAsync(
                        currentFullReview,
                        cancellationToken
                    );

                    break;

                case 5:
                    return;
            }
        }
    }

    private async Task<AggregatedBackupReport?>
        ReviewAllTenantsAsync(
            CancellationToken cancellationToken)
    {
        WriteProgress(
            "Loading backup data..."
        );

        try
        {
            var report =
                await _backupAggregatorWorkflow
                    .ReviewAllTenantsAsync(
                        cancellationToken
                    );

            Console.WriteLine();

            _backupAggregatorReporter
                .Write(
                    report
                );

            Pause();

            return report;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteError(
                exception
            );

            Pause();

            return null;
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

        WriteProgress(
            "Loading backup data..."
        );

        try
        {
            var report =
                await _backupAggregatorWorkflow
                    .ReviewTenantAsync(
                        tenantName,
                        cancellationToken
                    );

            Console.WriteLine();

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
            WriteError(
                exception
            );
        }

        Pause();
    }

    private void PreviewTicket(
        AggregatedBackupReport? currentFullReview)
    {
        if (currentFullReview is null)
        {
            WriteFullReviewRequired();

            Pause();

            return;
        }

        try
        {
            var draft =
                _backupAggregatorWorkflow
                    .CreateTicketDraft(
                        currentFullReview
                    );

            _ticketDraftPreviewer
                .Write(
                    draft
                );
        }
        catch (Exception exception)
        {
            WriteError(
                exception
            );
        }

        Pause();
    }

    private async Task SubmitTicketAsync(
        AggregatedBackupReport? currentFullReview,
        CancellationToken cancellationToken)
    {
        if (currentFullReview is null)
        {
            WriteFullReviewRequired();

            Pause();

            return;
        }

        Console.WriteLine(
            "This will submit a ticket based on the current full review."
        );

        Console.WriteLine();

        if (!ReadConfirmation(
                "Submit ticket? [y/N]: ",
                defaultValue:
                    false
            ))
        {
            Console.WriteLine();

            Console.WriteLine(
                "Ticket submission cancelled."
            );

            Console.WriteLine();

            Pause();

            return;
        }

        Console.WriteLine();

        WriteProgress(
            "Submitting ticket..."
        );

        try
        {
            var ticket =
                await _backupAggregatorWorkflow
                    .SubmitTicketAsync(
                        currentFullReview,
                        cancellationToken
                    );

            Console.WriteLine();

            WriteCreatedTicket(
                ticket
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteError(
                exception
            );
        }

        Pause();
    }

    private async Task RunBackupHealthMenuAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode =
                await _backupHealthWorkflow
                    .GetModeAsync(
                        cancellationToken
                    );

            Console.WriteLine(
                "Backup Health"
            );

            Console.WriteLine(
                "============="
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Mode: {FormatMode(mode)}"
            );

            Console.WriteLine();

            Console.WriteLine(
                "1. Run health evaluation"
            );

            Console.WriteLine(
                "2. Back"
            );

            Console.WriteLine();

            var choice =
                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        2
                );

            Console.WriteLine();

            switch (choice)
            {
                case 1:
                    await ExecuteBackupHealthAsync(
                        cancellationToken
                    );

                    break;

                case 2:
                    return;
            }
        }
    }

    private async Task ExecuteBackupHealthAsync(
        CancellationToken cancellationToken)
    {
        WriteProgress(
            "Evaluating backup health..."
        );

        try
        {
            var result =
                await _backupHealthWorkflow
                    .EvaluateAsync(
                        cancellationToken
                    );

            Console.WriteLine();

            WriteBackupHealthResult(
                result
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteError(
                exception
            );
        }

        Pause();
    }

    private void WriteBackupHealthResult(
        BackupHealthResult result)
    {
        _backupHealthReporter
            .Write(
                result.Snapshot.Resources,
                result.Exceptions,
                result.Snapshot.Diagnostics
            );
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