using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Modules.BackupAggregator;

public sealed class BackupAggregatorModule
    : ISupportToolkitModule
{
    public string Command =>
        "backup-aggregator";

    public string Description =>
        "Review Acronis backup inventory and surface only exceptions.";

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment();

        if (runtimeOptions.Mode == SupportToolkitMode.Fixture)
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

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            BackupAggregatorProductionSession.Create(
                logger
            );

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
            .Write(report);

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
            .Write(report);

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

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production or fixture mode:"
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
            "Fixture mode:"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_MODE=fixture"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator"
        );
    }
}