using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
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
            if (args.Length > 0)
            {
                Console.Error.WriteLine(
                    "ERROR: inventory-review commands require " +
                    "SUPPORTTOOLKIT_MODE=production."
                );

                Console.Error.WriteLine();

                PrintUsage();

                return 1;
            }

            return RunFixture();
        }

        if (args.Length != 2
            || !string.Equals(
                args[0],
                "inventory-review",
                StringComparison.OrdinalIgnoreCase
            ))
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

    private static int RunFixture()
    {
        var report =
            new BackupAggregatorFixtureReviewService()
                .BuildReport();

        new ConsoleBackupAggregatorReporter()
            .Write(report);

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production:"
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