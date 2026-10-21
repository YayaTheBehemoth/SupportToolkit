
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
    public const string ModuleCommand = "backup-aggregator";

    private readonly BackupAggregatorWorkflow _workflow;

    private readonly BackupAggregatorScopedWorkflow? _scopedWorkflow;

    public string Command => ModuleCommand;

    public string Description =>
        "Review Acronis backup inventory and surface only exceptions.";

    public BackupAggregatorModule(
        BackupAggregatorWorkflow workflow,
        BackupAggregatorScopedWorkflow? scopedWorkflow = null)
    {
        _scopedWorkflow =
            scopedWorkflow;

        _workflow =
            workflow
            ?? throw new ArgumentNullException(nameof(workflow));
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (IsTicketPreviewCommand(args))
        {
            return await RunTicketPreviewAsync();
        }

        if (IsTicketSubmitCommand(args))
        {
            return await RunTicketSubmitAsync();
        }

        if (IsScopedReviewCommand(args))
        {
            return await RunScopedReviewAsync();
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

            if (mode != SupportToolkitMode.Fixture)
            {
                PrintUsage();
                return 1;
            }

            var fixtureReport =
                await _workflow.ReviewAllTenantsAsync();

            WriteReport(fixtureReport);
            return GetExitCode(fixtureReport);
        }

        if (!IsInventoryReviewCommand(args))
        {
            PrintUsage();
            return 1;
        }

        /*
         * Runtime configuration decides where data comes from.
         * CLI arguments decide what should be reviewed.
         */
        var report =
            string.Equals(
                args[1],
                "--all",
                StringComparison.OrdinalIgnoreCase)
                ? await _workflow.ReviewAllTenantsAsync()
                : await _workflow.ReviewTenantAsync(args[1]);

        WriteReport(report);

        return GetExitCode(report);
    }

    /// <summary>
    /// Opt-in review limited to the tenants selected by the shared
    /// monitoring scope. Never used by the default commands.
    /// </summary>
    private async Task<int> RunScopedReviewAsync()
    {
        if (_scopedWorkflow is null)
        {
            Console.Error.WriteLine(
                "Scoped review is not available in this host.");

            return 1;
        }

        var result =
            await _scopedWorkflow.ReviewAsync(
                DateOnly.FromDateTime(DateTime.Today));

        new ConsoleScopedReviewReporter()
            .Write(Console.Out, result);

        Console.WriteLine();

        WriteReport(result.Inventory);

        return result.IsOverallComplete
            ? 0
            : 2;
    }

    private async Task<int> RunTicketPreviewAsync()
    {
        var draft =
            await _workflow.CreateTicketDraftAsync();

        WriteTicketPreview(draft);

        return 0;
    }

    private async Task<int> RunTicketSubmitAsync()
    {
        var ticket =
            await _workflow.SubmitTicketAsync();

        WriteCreatedTicket(ticket);

        return 0;
    }

    private static void WriteReport(
        AggregatedBackupReport report)
    {
        new ConsoleBackupAggregatorReporter()
            .Write(report);
    }

    private static void WriteTicketPreview(
        TicketDraft draft)
    {
        new ConsoleTicketDraftPreviewer()
            .Write(draft);
    }

    private static void WriteCreatedTicket(
        CreatedTicket ticket)
    {
        ConsoleCreatedTicketReporter.Write(
            Console.Out,
            ticket);
    }

    private static int GetExitCode(
        AggregatedBackupReport report)
    {
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
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScopedReviewCommand(
        string[] args)
    {
        return args.Length == 1
            && string.Equals(
                args[0],
                "scoped-review",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTicketPreviewCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "ticket",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                args[1],
                "preview",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTicketSubmitCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "ticket",
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                args[1],
                "submit",
                StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Backup Aggregator");
        Console.WriteLine();

        Console.WriteLine("Inventory review:");

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "inventory-review <tenant-name>");

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "inventory-review --all");

        Console.WriteLine();

        Console.WriteLine("Ticket workflow:");

        Console.WriteLine(
            "  SupportToolkit backup-aggregator ticket preview");

        Console.WriteLine(
            "  SupportToolkit backup-aggregator ticket submit");

        Console.WriteLine();

        Console.WriteLine("Scoped review (opt-in, Business Central):");

        Console.WriteLine(
            "  SupportToolkit backup-aggregator scoped-review");

        Console.WriteLine(
            "Reviews only tenants the monitoring scope selects. " +
            "Does not replace inventory-review and cannot create tickets.");

        Console.WriteLine();

        Console.WriteLine(
            "Ticket preview uses the same BackupAggregator runtime mode " +
            "as the inventory review.");

        Console.WriteLine(
            "Ticket submission requires a configured Zendesk connection " +
            "and an explicitly selected destination.");

        Console.WriteLine();

        Console.WriteLine(
            "The active SupportToolkit profile controls whether " +
            "BackupAggregator uses fixture or production data.");

        Console.WriteLine();

        Console.WriteLine("Optional runtime override:");

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=fixture");

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=production");

        Console.WriteLine();

        Console.WriteLine("Fixture mode also supports:");
        Console.WriteLine("  SupportToolkit backup-aggregator");
    }
}
