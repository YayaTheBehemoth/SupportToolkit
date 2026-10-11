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
}
