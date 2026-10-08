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
        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment();

        if (runtimeOptions.Mode == SupportToolkitMode.Fixture)
        {
            return RunFixture();
        }

        if (args.Length == 0
            || args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        if (!string.Equals(
                args[0],
                "inventory-review",
                StringComparison.OrdinalIgnoreCase
            )
            || args.Length != 2)
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
            await session.ReviewService.ReviewTenantAsync(
                args[1]
            );

        new ConsoleBackupAggregatorReporter()
            .Write(report);

        return 0;
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
