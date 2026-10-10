using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.Ticketing.Models;
using SupportToolkit.Modules.Ticketing.Reporting;
using SupportToolkit.Modules.Ticketing.Services;
using SupportToolkit.Modules.Ticketing.Sources;

namespace SupportToolkit.Modules.Ticketing;

public sealed class TicketingModule
    : ISupportToolkitModule
{
    public const string ModuleCommand =
        "ticketing";

    private readonly IReadOnlyList<ITicketDraftSource>
        _draftSources;

    public string Command =>
        ModuleCommand;

    public string Description =>
        "Preview and create tickets through configured ticketing workflows.";

    public TicketingModule(
        IEnumerable<ITicketDraftSource> draftSources)
    {
        ArgumentNullException.ThrowIfNull(
            draftSources
        );

        _draftSources =
            draftSources.ToList();

        ValidateSourceCommands(
            _draftSources
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

        if (IsPreviewCommand(
                args))
        {
            return await RunPreviewAsync(
                args[1]
            );
        }

        if (IsSubmitCommand(
                args))
        {
            return await RunSubmitAsync(
                args[1]
            );
        }

        if (IsCreateTestTicketCommand(
                args))
        {
            return await RunCreateTestTicketAsync();
        }

        PrintUsage();

        return 1;
    }

    private async Task<int> RunPreviewAsync(
        string sourceCommand)
    {
        var source =
            FindDraftSource(
                sourceCommand
            );

        if (source is null)
        {
            WriteUnknownSourceError(
                sourceCommand
            );

            return 1;
        }

        /*
         * Preview never constructs an external ticket provider.
         *
         * The selected source independently decides where its input data comes
         * from according to the source module's own runtime configuration.
         */
        var draft =
            await source.CreateDraftAsync();

        new ConsoleTicketDraftPreviewer()
            .Write(
                draft
            );

        return 0;
    }

    private async Task<int> RunSubmitAsync(
        string sourceCommand)
    {
        var source =
            FindDraftSource(
                sourceCommand
            );

        if (source is null)
        {
            WriteUnknownSourceError(
                sourceCommand
            );

            return 1;
        }

        if (!WritesAreEnabled())
        {
            return 1;
        }

        if (!TicketingProductionModeIsEnabled())
        {
            return 1;
        }

        /*
         * Draft generation remains independent from the ticket provider.
         *
         * For example, BackupAggregator may be in fixture mode while
         * Ticketing is configured to submit that synthetic draft to the real
         * Zendesk sandbox.
         */
        var draft =
            await source.CreateDraftAsync();

        var idempotencyKey =
            TicketIdempotencyKeyFactory.Create(
                source.Command,
                draft
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            TicketingProductionSession.Create(
                logger
            );

        var ticket =
            await session.TicketProvider
                .CreateTicketAsync(
                    draft,
                    idempotencyKey
                );

        WriteCreatedTicket(
            ticket,
            source.Command
        );

        return 0;
    }

    private static async Task<int>
        RunCreateTestTicketAsync()
    {
        if (!WritesAreEnabled())
        {
            return 1;
        }

        if (!TicketingProductionModeIsEnabled())
        {
            return 1;
        }

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            TicketingProductionSession.Create(
                logger
            );

        var idempotencyKey =
            $"supporttoolkit-smoke-" +
            $"{Guid.NewGuid():N}";

        var ticket =
            await session.TicketProvider
                .CreateTicketAsync(
                    new TicketDraft
                    {
                        Subject =
                            "SupportToolkit ticketing smoke test",

                        Body =
                            "Synthetic ticket created by " +
                            "SupportToolkit while validating " +
                            "the ticketing provider."
                    },
                    idempotencyKey
                );

        WriteCreatedTicket(
            ticket,
            "provider-smoke-test"
        );

        return 0;
    }

    private ITicketDraftSource? FindDraftSource(
        string sourceCommand)
    {
        return _draftSources.FirstOrDefault(
            candidate =>
                string.Equals(
                    candidate.Command,
                    sourceCommand,
                    StringComparison.OrdinalIgnoreCase
                )
        );
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

    private static bool TicketingProductionModeIsEnabled()
    {
        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment(
                ModuleCommand
            );

        if (runtimeOptions.Mode
            == SupportToolkitMode.Production)
        {
            return true;
        }

        Console.Error.WriteLine(
            "ERROR: ticket submission requires " +
            "SUPPORTTOOLKIT_TICKETING_MODE=production."
        );

        return false;
    }

    private static void WriteCreatedTicket(
        CreatedTicket ticket,
        string sourceCommand)
    {
        Console.WriteLine();

        Console.WriteLine(
            "Ticket submitted successfully."
        );

        Console.WriteLine(
            $"Source:    {sourceCommand}"
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

    private void WriteUnknownSourceError(
        string sourceCommand)
    {
        Console.Error.WriteLine(
            $"ERROR: unknown ticket draft source '{sourceCommand}'."
        );

        Console.Error.WriteLine();

        PrintUsage();
    }

    private static bool IsPreviewCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "preview",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsSubmitCommand(
        string[] args)
    {
        return args.Length == 2
            && string.Equals(
                args[0],
                "submit",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsCreateTestTicketCommand(
        string[] args)
    {
        return args.Length == 1
            && string.Equals(
                args[0],
                "create-test-ticket",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private void PrintUsage()
    {
        Console.WriteLine(
            "Ticketing"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Preview:"
        );

        Console.WriteLine(
            "  SupportToolkit ticketing preview <source>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Submit:"
        );

        Console.WriteLine(
            "  SupportToolkit ticketing submit <source>"
        );

        if (_draftSources.Count > 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                "Available sources:"
            );

            foreach (var source in _draftSources
                         .OrderBy(
                             source =>
                                 source.Command,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                Console.WriteLine(
                    $"  {source.Command,-20} {source.Description}"
                );
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "Provider smoke test:"
        );

        Console.WriteLine(
            "  SupportToolkit ticketing create-test-ticket"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Ticketing provider runtime:"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_TICKETING_MODE=fixture"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_TICKETING_MODE=production"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Write permission:"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_ALLOW_WRITES=true"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Preview never requires write permission or an external " +
            "ticketing provider."
        );
    }

    private static void ValidateSourceCommands(
        IReadOnlyList<ITicketDraftSource> sources)
    {
        var duplicate =
            sources
                .GroupBy(
                    source =>
                        source.Command,
                    StringComparer.OrdinalIgnoreCase
                )
                .FirstOrDefault(
                    group =>
                        group.Count() > 1
                );

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"More than one ticket draft source uses " +
                $"the command '{duplicate.Key}'."
            );
        }

        if (sources.Any(
                source =>
                    string.IsNullOrWhiteSpace(
                        source.Command
                    )
            ))
        {
            throw new InvalidOperationException(
                "Ticket draft source commands cannot be empty."
            );
        }
    }
}