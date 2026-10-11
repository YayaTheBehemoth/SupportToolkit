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
}
