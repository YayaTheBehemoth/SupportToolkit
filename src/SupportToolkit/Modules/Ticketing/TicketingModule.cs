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
    private readonly IReadOnlyList<ITicketDraftSource>
        _draftSources;

    public string Command =>
        "ticketing";

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
            _draftSources.FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.Command,
                        sourceCommand,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        if (source is null)
        {
            Console.Error.WriteLine(
                $"ERROR: unknown ticket draft source '{sourceCommand}'."
            );

            Console.Error.WriteLine();

            PrintUsage();

            return 1;
        }

        var draft =
            await source.CreateDraftAsync();

        new ConsoleTicketDraftPreviewer()
            .Write(
                draft
            );

        return 0;
    }

    private static async Task<int>
        RunCreateTestTicketAsync()
    {
        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment();

        if (runtimeOptions.Mode
            != SupportToolkitMode.Production)
        {
            Console.Error.WriteLine(
                "ERROR: create-test-ticket requires " +
                "SUPPORTTOOLKIT_MODE=production."
            );

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

        Console.WriteLine();

        Console.WriteLine(
            "Ticket created successfully."
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

        return 0;
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