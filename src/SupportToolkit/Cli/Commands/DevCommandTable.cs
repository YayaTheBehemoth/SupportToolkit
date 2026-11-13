using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// One developer diagnostic: its name after "dev", and how it runs. The
/// handler receives the whole command line and validates it itself, so
/// each command keeps its own usage text and exit codes.
/// </summary>
public sealed record DevCommand(
    string Name,
    Func<string[], Task<int>> RunAsync);

/// <summary>
/// The developer diagnostics (`SupportToolkit dev &lt;name&gt; ...`).
///
/// Commands that read a real system require an explicit --production;
/// the fixture-only ones access no external provider.
/// </summary>
public static class DevCommandTable
{
    public static IReadOnlyList<DevCommand> Create(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        var tenantDirectoryCommand =
            new AcronisTenantDirectoryDevCommand(
                configurationResolver);

        var usageProbeCommand =
            new AcronisUsageDevCommand(
                configurationResolver);

        var businessCentralDiagnosticsCommand =
            new BusinessCentralSubscriptionDevCommand(
                configurationResolver);

        var zendeskBulkDeleteCommand =
            new ZendeskBulkDeleteDevCommand(
                configurationResolver);

        return
        [
            /*
             * --production explicitly authorizes a read-only Acronis API call.
             * --names additionally permits customer names in console output.
             *
             * This is infrastructure diagnostics, not an operational module.
             */
            new(
                "acronis-tenants",
                async args =>
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
                }),

            /*
             * Zendesk bulk delete, sign-in, search and preview only.
             * Nothing is deleted. Interactive only.
             *
             * --production explicitly authorizes the interactive Zendesk
             * sign-in and read-only calls, using the profile's Zendesk
             * deletion connection (not the ticketing connection).
             */
            new(
                "zendesk-bulk-delete",
                async args =>
                {
                    if (!ZendeskBulkDeleteDevCommand.TryParseArguments(
                            args.Skip(2).ToArray(),
                            out var bulkDeleteOptions,
                            out var bulkDeleteError))
                    {
                        Console.Error.WriteLine(bulkDeleteError);
                        return 1;
                    }

                    return await zendeskBulkDeleteCommand.RunAsync(
                        Console.Out,
                        bulkDeleteOptions!);
                }),

            /*
             * --production explicitly authorizes read-only Acronis calls.
             * --names adds one row per customer tenant (customer names);
             * --out writes those rows to a file outside any git working tree.
             */
            new(
                "acronis-usage",
                async args =>
                {
                    if (!AcronisUsageDevCommand.TryParseArguments(
                            args.Skip(2).ToArray(),
                            out var usageOptions,
                            out var usageError))
                    {
                        Console.Error.WriteLine(usageError);
                        return 1;
                    }

                    return await usageProbeCommand.RunAsync(
                        Console.Out,
                        usageOptions!);
                }),

            /*
             * --production explicitly authorizes a read-only Business Central
             * read with an interactive sign-in. Output is aggregate counts only.
             */
            new(
                "bc-subscriptions",
                async args =>
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
                }),

            /*
             * --production explicitly authorizes read-only Business Central
             * (interactive sign-in) and Acronis reads. Nothing is stored.
             * --names additionally prints names and customer numbers.
             */
            new(
                "customer-matching",
                async args =>
                {
                    var matchingOptions =
                        args.Skip(2).ToArray();

                    var validMatchingArguments =
                        matchingOptions.Length is >= 1 and <= 3
                        && matchingOptions.Contains(
                            "--production",
                            StringComparer.OrdinalIgnoreCase)
                        && matchingOptions.All(
                            option =>
                                option.Equals(
                                    "--production",
                                    StringComparison.OrdinalIgnoreCase)
                                || option.Equals(
                                    "--names",
                                    StringComparison.OrdinalIgnoreCase)
                                || option.Equals(
                                    "--why",
                                    StringComparison.OrdinalIgnoreCase))
                        && matchingOptions.Distinct(
                            StringComparer.OrdinalIgnoreCase).Count()
                            == matchingOptions.Length;

                    if (!validMatchingArguments)
                    {
                        Console.Error.WriteLine(
                            "Usage: SupportToolkit dev customer-matching " +
                            "--production [--names] [--why]");

                        return 1;
                    }

                    return await new CustomerMatchingDevCommand(
                            configurationResolver)
                        .RunAsync(
                            Console.Out,
                            showNames: matchingOptions.Contains(
                                "--names",
                                StringComparer.OrdinalIgnoreCase),
                            explainUnmatched: matchingOptions.Contains(
                                "--why",
                                StringComparer.OrdinalIgnoreCase));
                }),

            /*
             * Fixture-only monitoring-scope investigation.
             *
             * No external providers are accessed.
             */
            new(
                "monitoring-scope",
                args =>
                {
                    if (args.Length != 2)
                    {
                        Console.Error.WriteLine(
                            "Usage: SupportToolkit dev monitoring-scope"
                        );

                        return Task.FromResult(1);
                    }

                    return Task.FromResult(
                        new MonitoringScopeFixtureCommand()
                            .Run(
                                Console.Out,
                                DateOnly.FromDateTime(
                                    DateTime.Today
                                )
                            ));
                }),

            /*
             * Fixture-only customer mapping and shadow reconciliation.
             *
             * No external providers are accessed and nothing is persisted.
             */
            new(
                "customer-mapping",
                async args =>
                {
                    if (args.Length != 2)
                    {
                        Console.Error.WriteLine(
                            "Usage: SupportToolkit dev customer-mapping"
                        );

                        return 1;
                    }

                    return await new CustomerMappingFixtureCommand()
                        .RunAsync(
                            Console.Out,
                            DateOnly.FromDateTime(
                                DateTime.Today
                            )
                        );
                }),

            /*
             * Fixture-only scoped BackupAggregator execution.
             *
             * Uses synthetic tenant selection and in-memory inventory
             * providers. Does not affect production module dispatch.
             */
            new(
                "scoped-backup-review",
                async args =>
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
                })
        ];
    }

    /// <summary>
    /// The command named by `dev &lt;name&gt; ...`, or null when the line
    /// is not one (an unknown name falls through to module dispatch, as
    /// before).
    /// </summary>
    public static DevCommand? Find(
        IReadOnlyList<DevCommand> commands,
        string[] args)
    {
        if (args.Length < 2
            || !string.Equals(
                args[0],
                "dev",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return commands.FirstOrDefault(
            command => string.Equals(
                command.Name,
                args[1],
                StringComparison.OrdinalIgnoreCase));
    }
}
