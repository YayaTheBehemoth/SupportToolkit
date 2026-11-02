using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
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
    ///
    /// This is a console adapter. What may be done with a review, and
    /// when, is decided by BackupReviewSession; this class only prompts
    /// and prints.
    /// </summary>
    private async Task RunBackupAggregatorMenuAsync(
        CancellationToken cancellationToken)
    {
        // The review lives as long as the operator stays in this menu.
        var session =
            new BackupReviewSession(
                _backupAggregatorWorkflow,
                _backupAggregatorScopedWorkflow);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var availability =
                await session.GetAvailabilityAsync(
                    cancellationToken);

            Console.WriteLine(
                "Backup Aggregator"
            );

            Console.WriteLine(
                "================="
            );

            Console.WriteLine();

            Console.WriteLine(
                $"Mode:             {FormatMode(availability.Mode)}"
            );

            if (!availability.IsAvailable)
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
                FormatScopedReviewState(session.CurrentSummary)
            );

            Console.WriteLine();

            Console.WriteLine(
                session.CurrentReview is null
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
                    await ReviewScopedAsync(
                        session,
                        cancellationToken
                    );

                    break;

                case 2:
                    PreviewScopedTicket(
                        session
                    );

                    break;

                case 3:
                    await SubmitScopedTicketAsync(
                        session,
                        cancellationToken
                    );

                    break;

                default:
                    return;
            }
        }
    }

    private static string FormatScopedReviewState(
        ReviewSummary? review)
    {
        if (review is null)
        {
            return "not run";
        }

        return
            $"{review.TenantCount} tenant(s), " +
            $"{review.FindingsCount} finding(s), " +
            (review.IsComplete
                ? "complete"
                : "incomplete");
    }

    private async Task ReviewScopedAsync(
        BackupReviewSession session,
        CancellationToken cancellationToken)
    {
        WriteProgress(
            "Loading monitoring scope and backup data..."
        );

        try
        {
            var result =
                await session.RunReviewAsync(
                    cancellationToken
                );

            new ConsoleScopedReviewReporter(
                    OperationalLogger.FromEnvironment().DebugEnabled)
                .Write(Console.Out, result);

            Console.WriteLine();

            _backupAggregatorReporter
                .Write(result.Inventory);

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
    }

    private void PreviewScopedTicket(
        BackupReviewSession session)
    {
        try
        {
            var preparation =
                session.PreviewTicket();

            if (!preparation.IsReady)
            {
                WriteTicketNotReady(
                    preparation.NotReady);

                Pause();

                return;
            }

            _ticketDraftPreviewer
                .Write(
                    preparation.Value
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

    /// <summary>
    /// The confirmation-and-submission flow: the session validates the
    /// draft and the destination before any prompt, the operator
    /// confirms, and exactly the confirmed plan is submitted.
    /// </summary>
    private async Task SubmitScopedTicketAsync(
        BackupReviewSession session,
        CancellationToken cancellationToken)
    {
        TicketPreparation<TicketSubmissionPlan> preparation;

        try
        {
            // Fails before prompting if there is no review, the review
            // is incomplete, or the selected Zendesk destination and
            // credentials are invalid.
            preparation =
                await session.PrepareSubmissionAsync(
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

        if (!preparation.IsReady)
        {
            WriteTicketNotReady(
                preparation.NotReady);

            Pause();

            return;
        }

        var plan = preparation.Value;
        var destination = plan.Destination;
        var review = plan.Review;

        Console.WriteLine("Confirm ticket submission");
        Console.WriteLine("-------------------------");
        Console.WriteLine();
        Console.WriteLine($"Zendesk instance: https://{destination.InstanceHost}");
        Console.WriteLine($"Destination alias: {destination.DestinationName}");
        Console.WriteLine($"Zendesk group ID: {destination.GroupId}");
        Console.WriteLine(
            $"Review: {review.TenantCount}/" +
            $"{review.TenantsAttempted} monitored tenants, " +
            $"{review.FindingsCount} findings, " +
            $"{review.UnclassifiedCount} unclassified");
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
                await session.SubmitAsync(
                    plan,
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

    /// <summary>
    /// Prints why a ticket cannot be prepared, worded as before.
    /// </summary>
    private void WriteTicketNotReady(
        TicketNotReady notReady)
    {
        switch (notReady.Kind)
        {
            case TicketNotReadyKind.NoReview:
                Console.WriteLine(
                    "Run the monitored review first (option 1), " +
                    "then try again."
                );

                Console.WriteLine();

                break;

            case TicketNotReadyKind.ReviewIncomplete:
                WriteError(
                    new InvalidOperationException(
                        "No ticket can be created because the " +
                        "monitored review is not complete: " +
                        string.Join(" ", notReady.Reasons)));

                break;

            default:
                WriteError(
                    new InvalidOperationException(
                        string.Join(" ", notReady.Reasons)));

                break;
        }
    }
}
