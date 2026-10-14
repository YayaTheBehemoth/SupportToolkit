
using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Modules;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Modules.BackupHealth.Services;
using SupportToolkit.Modules.Compliance;
using SupportToolkit.Ui.Interactive;

var configurationStore =
    JsonSupportToolkitConfigurationStore
        .CreateDefault();

ISecretStore secretStore =
    new WindowsCredentialStore();

var configurationResolver =
    new SupportToolkitConfigurationResolver(
        configurationStore,
        secretStore
    );

var configureCommand =
    new ConfigureCommand(
        configurationStore,
        secretStore
    );

var backupAggregatorWorkflow =
    new BackupAggregatorWorkflow(
        configurationResolver
    );

var backupHealthWorkflow =
    new BackupHealthWorkflow(
        configurationResolver
    );

ISupportToolkitModule[] modules =
[
    new BackupHealthModule(
        backupHealthWorkflow
    ),

    new BackupAggregatorModule(
        backupAggregatorWorkflow
    ),

    new ComplianceModule()
];

var interactiveShell =
    new SupportToolkitInteractiveShell(
        configurationStore,
        configureCommand,
        backupAggregatorWorkflow,
        backupHealthWorkflow
    );

var firstRunStartup =
    new FirstRunStartup(
        configurationStore
    );

var httpErrorContext =
    ResolveHttpErrorContext(args);

try
{
    if (args.Length == 0)
    {
        return await firstRunStartup.RunAsync(
            () => configureCommand.RunAsync(
                Array.Empty<string>()
            ),
            () => interactiveShell.RunAsync()
        );
    }

    if (args[0] is "--help" or "-h")
    {
        PrintUsage(modules);
        return 0;
    }

    /*
     * Host-level commands are handled before operational
     * module dispatch.
     *
     * Configuration is application infrastructure,
     * not an ISupportToolkitModule.
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

    var command = args[0];

    var module = modules.FirstOrDefault(
        candidate => string.Equals(
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

        PrintUsage(modules);

        return 1;
    }

    return await module.RunAsync(
        args[1..]
    );
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        "ERROR: " +
        ConsoleErrorFormatter.Format(
            exception,
            httpErrorContext
        )
    );

    if (IsDebugEnabled())
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(exception);
    }

    return 1;
}

static HttpErrorContext ResolveHttpErrorContext(
    string[] commandLine)
{
    if (commandLine.Length == 0)
    {
        // Interactive workflows may use different providers.
        return HttpErrorContext.Generic;
    }

    var command = commandLine[0];

    if (string.Equals(
            command,
            ComplianceModule.ModuleCommand,
            StringComparison.OrdinalIgnoreCase))
    {
        return HttpErrorContext.ComplianceDevProxy;
    }

    if (string.Equals(
            command,
            BackupHealthModule.ModuleCommand,
            StringComparison.OrdinalIgnoreCase))
    {
        return HttpErrorContext.Acronis;
    }

    if (string.Equals(
            command,
            BackupAggregatorModule.ModuleCommand,
            StringComparison.OrdinalIgnoreCase))
    {
        // Ticket workflows may contact Acronis and Zendesk.
        // Without provider-level failure context, do not
        // attribute their HTTP errors to either service.
        if (commandLine.Length > 1
            && string.Equals(
                commandLine[1],
                "ticket",
                StringComparison.OrdinalIgnoreCase))
        {
            return HttpErrorContext.Generic;
        }

        return HttpErrorContext.Acronis;
    }

    return HttpErrorContext.Generic;
}

static void PrintUsage(
    IEnumerable<ISupportToolkitModule> modules)
{
    Console.WriteLine(
        "SupportToolkit"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Interactive:"
    );

    Console.WriteLine(
        "  SupportToolkit"
    );

    Console.WriteLine();

    Console.WriteLine(
        "Raw CLI:"
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
