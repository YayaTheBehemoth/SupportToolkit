
using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Modules;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Modules.BackupHealth.Services;
using SupportToolkit.Ui.Interactive;

var configurationStore =
    JsonSupportToolkitConfigurationStore
        .CreateDefault();

ISecretStore secretStore =
    new WindowsCredentialStore();

var configurationResolver =
    new SupportToolkitConfigurationResolver(
        configurationStore,
        secretStore);

var configureCommand =
    new ConfigureCommand(
        configurationStore,
        secretStore);

var tenantDirectoryCommand =
    new AcronisTenantDirectoryDevCommand(
        configurationResolver);

var businessCentralDiagnosticsCommand =
    new BusinessCentralSubscriptionDevCommand(
        configurationResolver);

var backupAggregatorWorkflow =
    new BackupAggregatorWorkflow(
        configurationResolver);

var backupAggregatorScopedWorkflow =
    new BackupAggregatorScopedWorkflow(
        configurationResolver);

var backupHealthWorkflow =
    new BackupHealthWorkflow(
        configurationResolver);

ISupportToolkitModule[] modules =
[
    new BackupHealthModule(
        backupHealthWorkflow),

    new BackupAggregatorModule(
        backupAggregatorWorkflow,
        backupAggregatorScopedWorkflow)
];

var interactiveShell =
    new SupportToolkitInteractiveShell(
        configurationStore,
        configureCommand,
        backupAggregatorWorkflow,
        backupHealthWorkflow,
        backupAggregatorScopedWorkflow);

var firstRunStartup =
    new FirstRunStartup(
        configurationStore);

try
{
    if (args.Length == 0)
    {
        return await firstRunStartup.RunAsync(
            () => configureCommand.RunAsync(
                Array.Empty<string>()),
            () => interactiveShell.RunAsync());
    }

    if (args[0] is "--help" or "-h")
    {
        PrintUsage(modules);
        return 0;
    }

    /*
     * Host-level developer utility.
     *
     * --production explicitly authorizes a read-only Acronis API call.
     * --names additionally permits customer names in console output.
     *
     * This is infrastructure diagnostics, not an operational module.
     */
    if (args.Length >= 2
        && string.Equals(
            args[0],
            "dev",
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            args[1],
            "acronis-tenants",
            StringComparison.OrdinalIgnoreCase))
    {
        var validArguments =
            (args.Length == 3 || args.Length == 4)
            && string.Equals(
                args[2],
                "--production",
                StringComparison.OrdinalIgnoreCase)
            && (args.Length == 3
                || string.Equals(
                    args[3],
                    "--names",
                    StringComparison.OrdinalIgnoreCase));

        if (!validArguments)
        {
            Console.Error.WriteLine(
                "Usage: SupportToolkit dev acronis-tenants " +
                "--production [--names]");
            return 1;
        }

        return await tenantDirectoryCommand.RunAsync(
            Console.Out,
            showNames: args.Length == 4);
    }

    /*
     * Host-level developer utility.
     *
     * --production explicitly authorizes a read-only Business Central
     * read with an interactive sign-in. Output is aggregate counts only.
     */
    if (args.Length >= 2
        && string.Equals(
            args[0],
            "dev",
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            args[1],
            "bc-subscriptions",
            StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length != 3
            || !string.Equals(
                args[2],
                "--production",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "Usage: SupportToolkit dev bc-subscriptions " +
                "--production");

            return 1;
        }

        return await businessCentralDiagnosticsCommand.RunAsync(
            Console.Out);
    }

    /*
     * Fixture-only monitoring-scope investigation.
     *
     * No external providers are accessed.
     */
    if (args.Length >= 2
        && string.Equals(
            args[0],
            "dev",
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            args[1],
            "monitoring-scope",
            StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: SupportToolkit dev monitoring-scope"
            );

            return 1;
        }

        return new MonitoringScopeFixtureCommand()
            .Run(
                Console.Out,
                DateOnly.FromDateTime(
                    DateTime.Today
                )
            );
    }

    /*
     * Fixture-only scoped BackupAggregator execution.
     *
     * Uses synthetic tenant selection and in-memory inventory
     * providers. Does not affect production module dispatch.
     */
    if (args.Length >= 2
        && string.Equals(
            args[0],
            "dev",
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(
            args[1],
            "scoped-backup-review",
            StringComparison.OrdinalIgnoreCase))
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(
                "Usage: SupportToolkit dev scoped-backup-review"
            );

            return 1;
        }

        return await new ScopedBackupReviewFixtureCommand()
            .RunAsync(
                Console.Out,
                DateOnly.FromDateTime(
                    DateTime.Today
                )
            );
    }

    /*
     * Host-level configuration is handled before module dispatch.
     */
    if (string.Equals(
        args[0],
        "configure",
        StringComparison.OrdinalIgnoreCase))
    {
        return await configureCommand.RunAsync(
            args[1..]);
    }

    var command = args[0];

    var module =
        modules.FirstOrDefault(
            candidate =>
                string.Equals(
                    candidate.Command,
                    command,
                    StringComparison.OrdinalIgnoreCase));

    if (module is null)
    {
        Console.Error.WriteLine(
            $"Unknown command or module: {command}");

        Console.Error.WriteLine();

        PrintUsage(modules);

        return 1;
    }

    return await module.RunAsync(
        args[1..]);
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"ERROR: {ConsoleErrorFormatter.Format(exception)}");

    if (IsDebugEnabled())
    {
        Console.Error.WriteLine();

        /*
         * Do not print the raw exception object here.
         *
         * Exception.ToString() may expose URLs, credentials,
         * request data, or local paths.
         */
        Console.Error.WriteLine(
            ConsoleErrorFormatter.FormatDiagnostic(exception));
    }

    return 1;
}

static void PrintUsage(
    IEnumerable<ISupportToolkitModule> modules)
{
    Console.WriteLine("SupportToolkit");
    Console.WriteLine();

    Console.WriteLine("Interactive:");
    Console.WriteLine("  SupportToolkit");
    Console.WriteLine();

    Console.WriteLine("Raw CLI:");
    Console.WriteLine("  SupportToolkit <command> [options]");
    Console.WriteLine("  SupportToolkit <module> [options]");
    Console.WriteLine();

    Console.WriteLine("Application commands:");
    Console.WriteLine(
        "  configure            Configure persistent settings and credentials.");
    Console.WriteLine();

    Console.WriteLine("Developer diagnostics:");
    Console.WriteLine(
        "  dev acronis-tenants --production [--names]");
    Console.WriteLine(
        "  dev bc-subscriptions --production");
    Console.WriteLine();

    Console.WriteLine("  dev monitoring-scope");
    Console.WriteLine("  dev scoped-backup-review");
    Console.WriteLine();
    Console.WriteLine("Modules:");

    foreach (var module in modules)
    {
        Console.WriteLine(
            $"  {module.Command,-20} {module.Description}");
    }
}

static bool IsDebugEnabled()
{
    return string.Equals(
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
