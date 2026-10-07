using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Modules.BackupHealth.Reporting;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Modules.BackupHealth;

public sealed class BackupHealthModule
    : ISupportToolkitModule
{
    private static readonly TimeSpan StaleAfter =
        TimeSpan.FromHours(48);

    private static readonly DateTimeOffset FixtureNow =
        new(
            2026,
            10,
            1,
            12,
            0,
            0,
            TimeSpan.Zero
        );

    public string Command =>
        "backup-health";

    public string Description =>
        "Evaluate backup health across Acronis tenants.";

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length > 0)
        {
            throw new InvalidOperationException(
                $"The '{Command}' module does not accept arguments yet."
            );
        }

        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment();

        var logger =
            new OperationalLogger();

        logger.Info(
            $"Starting {Command} in " +
            $"{runtimeOptions.Mode.ToString().ToLowerInvariant()} mode."
        );

        return runtimeOptions.Mode switch
        {
            SupportToolkitMode.Fixture =>
                await RunFixtureAsync(
                    logger
                ),

            SupportToolkitMode.Production =>
                await RunProductionAsync(
                    logger
                ),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported SupportToolkit mode: " +
                    $"{runtimeOptions.Mode}."
                )
        };
    }

    private static async Task<int> RunFixtureAsync(
        OperationalLogger logger)
    {
        var fixtureDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Acronis"
            );

        var provider =
            new FixtureAcronisProvider(
                fixtureDirectory
            );

        var timeProvider =
            new FixedTimeProvider(
                FixtureNow
            );

        return await RunBackupHealthAsync(
            provider,
            timeProvider,
            logger
        );
    }

    private static async Task<int> RunProductionAsync(
        OperationalLogger logger)
    {
        var acronisOptions =
            AcronisOptions.FromEnvironment();

        using var innerHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect =
                    false
            };

        using var readOnlyHandler =
            new AcronisReadOnlyHandler(
                acronisOptions.DatacenterUrl,
                innerHandler
            );

        using var httpClient =
            new HttpClient(
                readOnlyHandler
            )
            {
                Timeout =
                    TimeSpan.FromSeconds(30)
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                acronisOptions,
                logger:
                    logger
            );

        var provider =
            new HttpAcronisProvider(
                apiClient,
                logger
            );

        return await RunBackupHealthAsync(
            provider,
            TimeProvider.System,
            logger
        );
    }

    private static async Task<int> RunBackupHealthAsync(
        IAcronisProvider provider,
        TimeProvider timeProvider,
        OperationalLogger logger)
    {
        var backupHealthService =
            new BackupHealthService(
                provider
            );

        var exceptionEngine =
            new BackupExceptionEngine(
                timeProvider,
                StaleAfter
            );

        var reporter =
            new ConsoleBackupHealthReporter();

        logger.Info(
            "Loading backup health snapshot."
        );

        var snapshot =
            await backupHealthService
                .GetSnapshotAsync();

        logger.Info(
            $"Snapshot loaded: " +
            $"{snapshot.Resources.Count} resource(s), " +
            $"{snapshot.Diagnostics.Count} diagnostic(s)."
        );

        var exceptions =
            exceptionEngine.Evaluate(
                snapshot.Resources
            );

        logger.Info(
            $"Exception evaluation completed: " +
            $"{exceptions.Count} exception(s)."
        );

        reporter.Write(
            snapshot.Resources,
            exceptions,
            snapshot.Diagnostics
        );

        return 0;
    }

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
