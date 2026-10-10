using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Reporting;
using SupportToolkit.Core.Ticketing.Services;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Modules.BackupAggregator;

public sealed class BackupAggregatorModule
    : ISupportToolkitModule
{
    public const string ModuleCommand =
        "backup-aggregator";

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public string Command =>
        ModuleCommand;

    public string Description =>
        "Review Acronis backup inventory and surface only exceptions.";

    public BackupAggregatorModule(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver)
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

        var mode =
            await _configurationResolver
                .GetModuleModeAsync(
                    ModuleCommand
                );

        if (IsTicketPreviewCommand(
                args))
        {
            return await RunTicketPreviewAsync(
                mode
            );
        }

        if (IsTicketSubmitCommand(
                args))
        {
            return await RunTicketSubmitAsync(
                mode
            );
        }

        if (mode
            == SupportToolkitMode.Fixture)
        {
            return RunFixture(
                args
            );
        }

        if (!IsInventoryReviewCommand(
                args))
        {
            PrintUsage();

            return 1;
        }

        var resolvedConnection =
            await _configurationResolver
                .GetAcronisConnectionAsync();

        var acronisOptions =
            AcronisOptions.Create(
                resolvedConnection.DatacenterUrl,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            BackupAggregatorProductionSession.Create(
                acronisOptions,
                logger
            );

        /*
         * Runtime configuration decides where the data comes from.
         *
         * Command arguments still decide what should be reviewed.
         */
        var report =
            string.Equals(
                args[1],
                "--all",
                StringComparison.OrdinalIgnoreCase
            )
                ? await session.ReviewService
                    .ReviewAllTenantsAsync()

                : await session.ReviewService
                    .ReviewTenantAsync(
                        args[1]
                    );

        new ConsoleBackupAggregatorReporter()
            .Write(
                report
            );

        /*
         * Backup findings are valid report output and therefore do not make
         * the process fail.
         *
         * Incomplete tenant coverage does. Returning a non-zero exit code for
         * failed tenant reviews lets future automation distinguish a complete
         * review from a partial one.
         */
        return report.FailedTenantCount > 0
            ? 2
            : 0;
    }

    private async Task<int> RunTicketPreviewAsync(
        SupportToolkitMode mode)
    {
        var report =
            await BuildConsolidatedReportAsync(
                mode
            );

        var draft =
            new BackupReviewTicketDraftFactory()
                .Create(
                    report
                );

        new ConsoleTicketDraftPreviewer()
            .Write(
                draft
            );

        return 0;
    }

    private async Task<int> RunTicketSubmitAsync(
        SupportToolkitMode mode)
    {
        if (!WritesAreEnabled())
        {
            return 1;
        }

        var report =
            await BuildConsolidatedReportAsync(
                mode
            );

        var draft =
            new BackupReviewTicketDraftFactory()
                .Create(
                    report
                );

        var resolvedConnection =
            await _configurationResolver
                .GetZendeskConnectionAsync();

        var zendeskOptions =
            ZendeskOptions.Create(
                resolvedConnection.Subdomain,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            TicketingProductionSession.Create(
                zendeskOptions,
                logger
            );

        var idempotencyKey =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft
            );

        var ticket =
            await session.TicketProvider
                .CreateTicketAsync(
                    draft,
                    idempotencyKey
                );

        WriteCreatedTicket(
            ticket
        );

        return 0;
    }

    private async Task<AggregatedBackupReport>
        BuildConsolidatedReportAsync(
            SupportToolkitMode mode)
    {
        if (mode
            == SupportToolkitMode.Fixture)
        {
            return new BackupAggregatorFixtureReviewService()
                .BuildReport();
        }

        var resolvedConnection =
            await _configurationResolver
                .GetAcronisConnectionAsync();

        var acronisOptions =
            AcronisOptions.Create(
                resolvedConnection.DatacenterUrl,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            BackupAggregatorProductionSession.Create(
                acronisOptions,
                logger
            );

        return await session.ReviewService
            .ReviewAllTenantsAsync();
    }

    private static bool WritesAreEnabled()
    {
        var writeOptions =
            SupportToolkitWriteOptions
                .FromEnvironment();

        if (writeOptions.AllowWrites)
        {
            return true;
        }

        Console.Error.WriteLine(
            "ERROR: external writes are disabled."
        );

        Console.Error.WriteLine(
            "Set SUPPORTTOOLKIT_ALLOW_WRITES=true " +
            "to explicitly enable write operations."
        );

        return false;
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

    private static int RunFixture(
        string[] args)
    {
        /*
         * Bare fixture execution remains available as a quick development
         * shortcut, while the normal inventory-review command surface mirrors
         * production behavior.
         */
        if (args.Length != 0
            && !IsInventoryReviewCommand(
                args))
        {
            PrintUsage();

            return 1;
        }

        var completeReport =
            new BackupAggregatorFixtureReviewService()
                .BuildReport();

        AggregatedBackupReport report;

        if (args.Length == 0
            || string.Equals(
                args[1],
                "--all",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            report =
                completeReport;
        }
        else
        {
            var matches =
                completeReport.Tenants
                    .Where(
                        tenant =>
                            string.Equals(
                                tenant.TenantName,
                                args[1],
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .ToList();

            if (matches.Count == 0)
            {
                Console.Error.WriteLine(
                    $"ERROR: fixture tenant '{args[1]}' was not found."
                );

                return 1;
            }

            if (matches.Count > 1)
            {
                Console.Error.WriteLine(
                    $"ERROR: more than one fixture tenant named " +
                    $"'{args[1]}' was found."
                );

                return 1;
            }

            report =
                new AggregatedBackupReport
                {
                    Tenants =
                    [
                        matches[0]
                    ]
                };
        }

        new ConsoleBackupAggregatorReporter()
            .Write(
                report
            );

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