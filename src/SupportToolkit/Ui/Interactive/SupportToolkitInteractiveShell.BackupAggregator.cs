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

            var scopedAvailable =
                await IsScopedReviewAvailableAsync(
                    cancellationToken);

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

            var backChoice = 5;

            if (scopedAvailable)
            {
                Console.WriteLine(
                    "5. Review monitored tenants (Business Central)"
                );

                backChoice = 6;
            }

            Console.WriteLine(
                $"{backChoice}. Back"
            );

            Console.WriteLine();

            var choice =
                ReadMenuChoice(
                    minimum:
                        1,
                    maximum:
                        backChoice
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

                case 5 when scopedAvailable:
                    await ReviewScopedAsync(
                        cancellationToken
                    );

                    break;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// The scoped review is offered only when the active profile
    /// selects a Business Central connection. Any configuration
    /// problem simply hides it; the regular menu is unaffected.
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

    private async Task ReviewScopedAsync(
        CancellationToken cancellationToken)
    {
        WriteProgress(
            "Loading monitoring scope and backup data..."
        );

        try
        {
            var result =
                await _backupAggregatorScopedWorkflow!
                    .ReviewAsync(
                        DateOnly.FromDateTime(DateTime.Today),
                        cancellationToken
                    );

            new ConsoleScopedReviewReporter()
                .Write(Console.Out, result);

            Console.WriteLine();

            _backupAggregatorReporter
                .Write(result.Inventory);
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

        ZendeskTicketDestinationPreview destination;

        try
        {
            // Fail before prompting if the report is incomplete or the
            // selected Zendesk destination/credentials are invalid.
            _backupAggregatorWorkflow.CreateTicketDraft(currentFullReview);

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
        Console.WriteLine(
            $"Review: {currentFullReview.TenantCount}/" +
            $"{currentFullReview.TenantsAttempted} tenants, " +
            $"{currentFullReview.FindingsCount} findings, " +
            $"{currentFullReview.UnknownRowsCount} unclassified");
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
                    .SubmitTicketToConfirmedDestinationAsync(
                        currentFullReview,
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
