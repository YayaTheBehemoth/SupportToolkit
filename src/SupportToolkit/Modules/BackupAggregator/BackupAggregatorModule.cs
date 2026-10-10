using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Modules;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Modules.BackupAggregator;

public sealed class BackupAggregatorModule
    : ISupportToolkitModule
{
    public const string ModuleCommand =
        "backup-aggregator";

    private readonly BackupAggregatorWorkflow
        _workflow;

    public string Command =>
        ModuleCommand;

    public string Description =>
        "Review Acronis backup inventory and surface only exceptions.";

    public BackupAggregatorModule(
        BackupAggregatorWorkflow workflow)
    {
        _workflow =
            workflow
            ?? throw new ArgumentNullException(
                nameof(workflow)
            );
    }

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        if (IsTicketPreviewCommand(
                args))
        {
            return await RunTicketPreviewAsync();
        }

        if (IsTicketSubmitCommand(
                args))
        {
            return await RunTicketSubmitAsync();
        }

        /*
         * Preserve the fixture-mode development shortcut:
         *
         * SupportToolkit backup-aggregator
         *
         * Production mode still requires an explicit inventory-review
         * operation.
         */
        if (args.Length == 0)
        {
            var mode =
                await _workflow.GetModeAsync();

            if (mode
                != SupportToolkitMode.Fixture)
            {
                PrintUsage();

                return 1;
            }

            var fixtureReport =
                await _workflow
                    .ReviewAllTenantsAsync();

            WriteReport(
                fixtureReport
            );

            return GetExitCode(
                fixtureReport
            );
        }

        if (!IsInventoryReviewCommand(
                args))
        {
            PrintUsage();

            return 1;
        }

        /*
         * Runtime configuration decides where data comes from.
         *
         * CLI arguments decide what should be reviewed.
         */
        var report =
            string.Equals(
                args[1],
                "--all",
                StringComparison.OrdinalIgnoreCase
            )
                ? await _workflow
                    .ReviewAllTenantsAsync()

                : await _workflow
                    .ReviewTenantAsync(
                        args[1]
                    );

        WriteReport(
            report
        );

        return GetExitCode(
            report
        );
    }

    private async Task<int> RunTicketPreviewAsync()
    {
        var draft =
            await _workflow
                .CreateTicketDraftAsync();

        WriteTicketPreview(
            draft
        );

        return 0;
    }

    private async Task<int> RunTicketSubmitAsync()
    {
        var ticket =
            await _workflow
                .SubmitTicketAsync();

        WriteCreatedTicket(
            ticket
        );

        return 0;
    }

    private static void WriteReport(
        AggregatedBackupReport report)
    {
        new ConsoleBackupAggregatorReporter()
            .Write(
                report
            );
    }

    private static void WriteTicketPreview(
        TicketDraft draft)
    {
        new ConsoleTicketDraftPreviewer()
            .Write(
                draft
            );
    }

    private static void WriteCreatedTicket(
        CreatedTicket ticket)
    {
        Console.WriteLine();

        Console.WriteLine(
            "Ticket submitted successfully."
        );

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

    private static int GetExitCode(
        AggregatedBackupReport report)
    {
        /*
         * Findings are legitimate report output.
         *
         * Failed tenant reviews indicate incomplete execution and therefore
         * retain the existing non-zero exit behavior.
         */
        return report.FailedTenantCount > 0
            ? 2
            : 0;
    }

    private static bool IsInventoryReviewCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "inventory-review",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsTicketPreviewCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "ticket",
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                args[1],
                "preview",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsTicketSubmitCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "ticket",
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                args[1],
                "submit",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Inventory review:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "inventory-review <tenant-name>"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "inventory-review --all"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Ticket workflow:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator ticket preview"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator ticket submit"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Ticket preview uses the same BackupAggregator runtime mode " +
            "as the inventory review."
        );

        Console.WriteLine(
            "Ticket submission additionally requires a configured Zendesk " +
            "connection and SUPPORTTOOLKIT_ALLOW_WRITES=true."
        );

        Console.WriteLine();

        Console.WriteLine(
            "The active SupportToolkit profile controls whether " +
            "BackupAggregator uses fixture or production data."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Optional runtime override:"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=fixture"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=production"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Fixture mode also supports:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator"
        );
    }
}