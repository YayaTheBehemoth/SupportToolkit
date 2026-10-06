using System.Diagnostics;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Modules.BackupHealth.Reporting;

var runStopwatch =
    Stopwatch.StartNew();

try
{
    var runtime =
        SupportToolkitRuntimeOptions
            .FromEnvironment();

    var debugMode =
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"
        );

    var debugEnabled =
        debugMode?.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase
        ) == true;

    var logger =
        new OperationalLogger(
            debugEnabled
        );

    Console.WriteLine(
        "SupportToolkit"
    );

    Console.WriteLine(
        $"Mode: {runtime.Mode}"
    );

    Console.WriteLine();

    logger.Info(
        $"Starting BackupHealth run in " +
        $"{runtime.Mode} mode."
    );

    IAcronisProvider provider;
    TimeProvider timeProvider;
    TimeSpan staleAfter;

    if (runtime.Mode
        == SupportToolkitMode.Fixture)
    {
        var fixtureDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Acronis"
            );

        provider =
            new FixtureAcronisProvider(
                fixtureDirectory
            );

        timeProvider =
            new FixedTimeProvider(
                new DateTimeOffset(
                    2026,
                    10,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.Zero
                )
            );

        staleAfter =
            TimeSpan.FromHours(48);
    }
    else
    {
        logger.Info(
            "Loading Acronis production configuration."
        );

        var acronisOptions =
            AcronisOptions.FromEnvironment();

        logger.Info(
            "Production transport: " +
            "read-only allowlist enabled, " +
            "HTTPS required, redirects disabled, " +
            "30-second request timeout."
        );

        var networkHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        var readOnlyHandler =
            new AcronisReadOnlyHandler(
                acronisOptions.DatacenterUrl,
                networkHandler
            );

        var httpClient =
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
                logger: logger
            );

        provider =
            new HttpAcronisProvider(
                apiClient,
                logger
            );

        timeProvider =
            TimeProvider.System;

        /*
         * Temporary threshold until production backup-plan scheduling has
         * been validated.
         */
        staleAfter =
            TimeSpan.FromHours(48);
    }

    logger.Info(
        "Loading BackupHealth source data."
    );

    var backupHealthService =
        new BackupHealthService(
            provider
        );

    var exceptionEngine =
        new BackupExceptionEngine(
            timeProvider,
            staleAfter
        );

    var reporter =
        new ConsoleBackupHealthReporter();

    var snapshot =
        await backupHealthService
            .GetSnapshotAsync();

    logger.Info(
        $"Normalization completed: " +
        $"{snapshot.Resources.Count} resource(s), " +
        $"{snapshot.Diagnostics.Count} diagnostic(s)."
    );

    logger.Info(
        "Evaluating backup exceptions."
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

    runStopwatch.Stop();

    logger.Info(
        $"BackupHealth run completed successfully " +
        $"in {runStopwatch.Elapsed.TotalSeconds:F2} seconds."
    );
}
catch (Exception exception)
{
    runStopwatch.Stop();

    Console.Error.WriteLine();

    Console.Error.WriteLine(
        $"ERROR: " +
        $"{ConsoleErrorFormatter.Format(exception)}"
    );

    Console.Error.WriteLine(
        $"Run aborted after " +
        $"{runStopwatch.Elapsed.TotalSeconds:F2} seconds."
    );

    var debugMode =
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"
        );

    if (debugMode?.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase
        ) == true)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            exception
        );
    }

    Environment.ExitCode = 1;
}

sealed class FixedTimeProvider
    : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedTimeProvider(
        DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }
}