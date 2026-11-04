
using SupportToolkit.Composition;
using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Modules;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.UsageReport;
using SupportToolkit.Modules.UsageReport.Services;
using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Ui;
using SupportToolkit.Ui.Web;
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

var devCommands =
    DevCommandTable.Create(
        configurationResolver);

var backupAggregatorWorkflow =
    BackupAggregatorComposition.Workflow(
        configurationResolver,
        ConsoleProgress.Write,
        ReviewRunMode.Manual);

var backupAggregatorScopedWorkflow =
    BackupAggregatorComposition.ScopedWorkflow(
        configurationResolver,
        progress: ConsoleProgress.Write,
        runMode: ReviewRunMode.Manual);

ISupportToolkitModule[] modules =
[
    new BackupAggregatorModule(
        backupAggregatorWorkflow,
        backupAggregatorScopedWorkflow),

    new UsageReportModule(
        new UsageReportSession(
            UsageReportComposition.Workflow(
                configurationResolver,
                ConsoleProgress.Write)))
];

var interactiveShell =
    new SupportToolkitInteractiveShell(
        configurationStore,
        configureCommand,
        backupAggregatorWorkflow,
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
     * The local web UI. The web host is built only here, so every other
     * command (and --help) never touches it. It listens on 127.0.0.1 only.
     */
    if (string.Equals(
            args[0],
            "serve",
            StringComparison.OrdinalIgnoreCase))
    {
        if (!WebServeArguments.TryParse(
                args.Skip(1).ToArray(),
                out var serveOptions,
                out var serveError))
        {
            Console.Error.WriteLine(serveError);
            return 1;
        }

        // The web host shows progress on the page, not in the console.
        // Each page has its own feed, so one run's messages never show
        // on another page.
        var webProgress = new ProgressFeed();
        var usageProgress = new ProgressFeed();

        var webWorkflow =
            BackupAggregatorComposition.Workflow(
                configurationResolver,
                webProgress.Write,
                ReviewRunMode.Manual);

        var webScopedWorkflow =
            BackupAggregatorComposition.ScopedWorkflow(
                configurationResolver,
                progress: webProgress.Write,
                runMode: ReviewRunMode.Manual);

        return await WebHostRunner.RunAsync(
            serveOptions!,
            new WebAppState(
                new ConfigurationStatusService(
                    configurationStore,
                    configurationResolver,
                    secretStore),
                new AggregatorWebSession(
                    new BackupReviewSession(
                        webWorkflow,
                        webScopedWorkflow),
                    webProgress),
                new BulkDeleteWebSession(
                    async token =>
                    {
                        try
                        {
                            var connection =
                                await configurationResolver
                                    .GetZendeskDeletionConnectionAsync(token);

                            return new BulkDeleteAvailability(
                                true,
                                connection.Subdomain,
                                null);
                        }
                        catch (InvalidOperationException exception)
                        {
                            return new BulkDeleteAvailability(
                                false,
                                null,
                                exception.Message);
                        }
                    },
                    async token =>
                    {
                        // Deletion is enabled here, and only here: this is
                        // the connection that can delete, used after the
                        // operator's typed confirmations.
                        var session =
                            await ZendeskBulkDeleteSession.CreateAsync(
                                configurationResolver,
                                enableDeletion: true,
                                cancellationToken: token);

                        return new BulkDeleteConnection(
                            session.CreateReviewSession(),
                            session);
                    }),
                new UsageReportWebSession(
                    new UsageReportSession(
                        UsageReportComposition.Workflow(
                            configurationResolver,
                            usageProgress.Write)),
                    usageProgress)));
    }

    /*
     * Developer diagnostics: `dev <name> ...`. Each command validates its
     * own arguments; an unknown name falls through to module dispatch.
     */
    var devCommand =
        DevCommandTable.Find(
            devCommands,
            args);

    if (devCommand is not null)
    {
        return await devCommand.RunAsync(args);
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
    Console.WriteLine(
        "  serve [--port <n>] [--no-browser]");
    Console.WriteLine(
        "                       Open the local web UI (127.0.0.1 only).");
    Console.WriteLine();

    Console.WriteLine("Developer diagnostics:");
    Console.WriteLine(
        "  dev acronis-tenants --production [--names]");
    Console.WriteLine(
        "  dev acronis-usage --production [--metric <name>]... [--tenant <text>] [--names [--out <path>]]");
    Console.WriteLine(
        "  dev bc-subscriptions --production");
    Console.WriteLine(
        "  dev zendesk-bulk-delete --production [--organization <name>] [--sender <name>] " +
        "[--requester <email>] [--domain <domain>] [--text <text>] " +
        "[--dry-run | --delete [--include <ids>] [--exclude <ids>] [--receipt-dir <path>]]");
    Console.WriteLine();

    Console.WriteLine("  dev monitoring-scope");
    Console.WriteLine("  dev scoped-backup-review");
    Console.WriteLine("  dev customer-mapping");
    Console.WriteLine(
        "  dev customer-matching --production [--names] [--why]");
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
