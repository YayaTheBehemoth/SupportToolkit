using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Modules;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Modules.Ticketing;
using SupportToolkit.Modules.Ticketing.Sources;

var configurationStore =
    JsonSupportToolkitConfigurationStore
        .CreateDefault();

ISecretStore secretStore =
    new WindowsCredentialStore();

var configureCommand =
    new ConfigureCommand(
        configurationStore,
        secretStore
    );

ITicketDraftSource[] ticketDraftSources =
[
    new BackupReviewTicketDraftSource()
];

ISupportToolkitModule[] modules =
[
    new BackupHealthModule(),

    new BackupAggregatorModule(),

    new TicketingModule(
        ticketDraftSources
    )
];

try
{
    if (args.Length == 0
        || args[0] is "--help" or "-h")
    {
        PrintUsage(
            modules
        );

        return 0;
    }

    /*
     * Host-level commands are handled before operational module dispatch.
     *
     * Configuration is application infrastructure rather than an
     * ISupportToolkitModule.
     */
    if (string.Equals(
            args[0],
            "configure",
            StringComparison.OrdinalIgnoreCase))
    {
        return await configureCommand.RunAsync(
            args[1..]
        );
    }

    var command =
        args[0];

    var module =
        modules.FirstOrDefault(
            candidate =>
                string.Equals(
                    candidate.Command,
                    command,
                    StringComparison.OrdinalIgnoreCase
                )
        );

    if (module is null)
    {
        Console.Error.WriteLine(
            $"Unknown command or module: {command}"
        );

        Console.Error.WriteLine();

        PrintUsage(
            modules
        );

        return 1;
    }

    return await module.RunAsync(
        args[1..]
    );
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"ERROR: {ConsoleErrorFormatter.Format(exception)}"
    );

    if (IsDebugEnabled())
    {
        Console.Error.WriteLine();

        Console.Error.WriteLine(
            exception
        );
    }

    return 1;
}

static void PrintUsage(
    IEnumerable<ISupportToolkitModule> modules)
{
    Console.WriteLine(
        "SupportToolkit"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Usage:"
    );

    Console.WriteLine(
        "  SupportToolkit <command> [options]"
    );

    Console.WriteLine(
        "  SupportToolkit <module> [options]"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Application commands:"
    );

    Console.WriteLine(
        "  configure            Configure persistent settings and credentials."
    );

    Console.WriteLine();

    Console.WriteLine(
        "Modules:"
    );

    foreach (var module in modules)
    {
        Console.WriteLine(
            $"  {module.Command,-20} {module.Description}"
        );
    }
}

static bool IsDebugEnabled()
{
    return string.Equals(
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"
        ),
        "true",
        StringComparison.OrdinalIgnoreCase
    );
}