using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Modules.BackupHealth.Reporting;
using SupportToolkit.Modules.BackupHealth.Services;

namespace SupportToolkit.Ui.Interactive;

public sealed partial class SupportToolkitInteractiveShell
{
    /// <summary>
    /// The interactive BackupAggregator offers the monitored review
    /// only: the tenants Business Central entitles, plus the
    /// operator's own. The unmonitored all-tenant review and its
    /// ticket remain available as command-line operations for
    /// development; they are not part of the operator's menu.
    /// </summary>
    private async Task RunBackupAggregatorMenuAsync(
        CancellationToken cancellationToken)
    {
        ScopedBackupInventoryReviewResult? currentScopedReview =
            null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode =
                await _backupAggregatorWorkflow
                    .GetModeAsync(
                        cancellationToken
                    );

            var scopedAvailable =
                mode == SupportToolkitMode.Fixture
                || await IsScopedReviewAvailableAsync(
                    cancellationToken);

            Console.WriteLine(
                "Backup Aggregator"
            );

            Console.WriteLine(
                "================="
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Mode:             {FormatMode(mode)}"
            );

            if (!scopedAvailable)
            {
                Console.WriteLine();

                Console.WriteLine(
                    "The monitored review needs a Business Central " +
                    "connection in the active profile."
                );

                Console.WriteLine(
                    "Select one with 'configure', or use fixture mode."
                );

                Console.WriteLine();

                Console.WriteLine(
                    "1. Back"
                );

                Console.WriteLine();

                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        1
                );

                Console.WriteLine();

                return;
            }

            Console.WriteLine(
                "Monitored review: " +
                FormatScopedReviewState(currentScopedReview)
            );

            Console.WriteLine();

            Console.WriteLine(
                currentScopedReview is null
                    ? "1. Review monitored tenants (Business Central)"
                    : "1. Refresh monitored review"
            );

            Console.WriteLine(
                "2. Preview ticket"
            );

            Console.WriteLine(
                "3. Submit ticket"
            );

            Console.WriteLine(
                "4. Back"
            );

            Console.WriteLine();

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
                    currentScopedReview =
                        await ReviewScopedAsync(
                            cancellationToken
                        );

                    break;

                case 2:
                    PreviewScopedTicket(
                        currentScopedReview
                    );

                    break;

                case 3:
                    await SubmitScopedTicketAsync(
                        currentScopedReview,
                        cancellationToken
                    );

                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// The monitored review is offered when the active profile selects
    /// a Business Central connection. Any configuration problem
    /// simply hides it.
    /// </summary>
    private async Task<bool> IsScopedReviewAvailableAsync(
        CancellationToken cancellationToken)
    {
        if (_backupAggregatorScopedWorkflow is null)
        {
            return false;
        }

        try
        {
            return await _backupAggregatorScopedWorkflow
                .IsEnabledAsync(cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string FormatScopedReviewState(
        ScopedBackupInventoryReviewResult? review)
    {
        if (review is null)
        {
            return "not run";
        }

        return
            $"{review.Inventory.TenantCount} tenant(s), " +
            $"{review.Inventory.FindingsCount} finding(s), " +
            (review.IsOverallComplete
                ? "complete"
                : "incomplete");
    }

    private async Task<ScopedBackupInventoryReviewResult?>
        ReviewScopedAsync(
            CancellationToken cancellationToken)
    {
        WriteProgress(
            "Loading monitoring scope and backup data..."
        );

        ScopedBackupInventoryReviewResult? reviewed = null;

        try
        {
            var result =
                await _backupAggregatorScopedWorkflow!
                    .ReviewAsync(
                        DateOnly.FromDateTime(DateTime.Today),
                        cancellationToken
                    );

            new ConsoleScopedReviewReporter(
                    OperationalLogger.FromEnvironment().DebugEnabled)
                .Write(Console.Out, result);

            Console.WriteLine();

            _backupAggregatorReporter
                .Write(result.Inventory);

            reviewed = result;

            if (result.TicketBlockers.Count > 0)
            {
                Console.WriteLine();

                Console.WriteLine(
                    "A ticket cannot be created from this review yet:");

                foreach (var blocker in result.TicketBlockers)
                {
                    Console.WriteLine($"  - {blocker}");
                }
            }
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

        return reviewed;
    }

    private async Task SubmitScopedTicketAsync(
        ScopedBackupInventoryReviewResult? currentScopedReview,
        CancellationToken cancellationToken)
    {
        if (currentScopedReview is null)
        {
            WriteScopedReviewRequired();

            Pause();

            return;
        }

        var inventory = currentScopedReview.Inventory;

        await ConfirmAndSubmitTicketAsync(
            () => _backupAggregatorWorkflow
                .CreateScopedTicketDraft(currentScopedReview),
            $"Review: {inventory.TenantCount}/" +
            $"{inventory.TenantsAttempted} monitored tenants, " +
            $"{inventory.FindingsCount} findings, " +
            $"{inventory.UnknownRowsCount} unclassified",
            (destination, token) => _backupAggregatorWorkflow
                .SubmitScopedTicketToConfirmedDestinationAsync(
                    currentScopedReview,
                    destination,
                    token),
            cancellationToken);
    }

    private void PreviewScopedTicket(
        ScopedBackupInventoryReviewResult? currentScopedReview)
    {
        if (currentScopedReview is null)
        {
            WriteScopedReviewRequired();

            Pause();

            return;
        }

        try
        {
            _ticketDraftPreviewer
                .Write(
                    _backupAggregatorWorkflow
                        .CreateScopedTicketDraft(currentScopedReview)
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

    private static void WriteScopedReviewRequired()
    {
        Console.WriteLine(
            "Run the monitored review first (option 1), then try again."
        );

        Console.WriteLine();
    }

    /// <summary>
    /// The confirmation-and-submission flow: validate the draft and the
    /// destination before prompting, confirm, then submit to exactly
    /// the confirmed destination.
    /// </summary>
    private async Task ConfirmAndSubmitTicketAsync(
        Action validateDraft,
        string reviewSummary,
        Func<ZendeskTicketDestinationPreview,
            CancellationToken,
            Task<CreatedTicket>> submit,
        CancellationToken cancellationToken)
    {
        ZendeskTicketDestinationPreview destination;

        try
        {
            // Fail before prompting if the report is incomplete or the
            // selected Zendesk destination/credentials are invalid.
            validateDraft();

            destination =
                await _backupAggregatorWorkflow.GetTicketSubmissionTargetAsync(
                    cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteError(exception);
            Pause();
            return;
        }

        Console.WriteLine("Confirm ticket submission");
        Console.WriteLine("-------------------------");
        Console.WriteLine();
        Console.WriteLine($"Zendesk instance: https://{destination.InstanceHost}");
        Console.WriteLine($"Destination alias: {destination.DestinationName}");
        Console.WriteLine($"Zendesk group ID: {destination.GroupId}");
        Console.WriteLine(reviewSummary);
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
                await submit(
                    destination,
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
}
