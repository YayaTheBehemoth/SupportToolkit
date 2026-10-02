using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Reporting;

try
{
    var runtime =
        SupportToolkitRuntimeOptions.FromEnvironment();

    Console.WriteLine("SupportToolkit");
    Console.WriteLine(
        $"Mode: {runtime.Mode}"
    );
    Console.WriteLine();

    IAcronisProvider provider;
    TimeProvider timeProvider;
    TimeSpan staleAfter;

    if (runtime.Mode == SupportToolkitMode.Fixture)
    {
        /*
         * Fixture mode uses deterministic synthetic data and a fixed clock.
         * This keeps local development and demonstrations repeatable.
         */
        var fixtureDirectory = Path.Combine(
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
        /*
         * Production mode must be explicitly enabled and requires credentials
         * to be supplied through environment variables.
         */
        var acronisOptions =
            AcronisOptions.FromEnvironment();

        /*
         * Redirects are deliberately disabled.
         *
         * A permitted Acronis request must not be allowed to redirect to
         * another location without going through the read-only policy again.
         */
        var networkHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        /*
         * The read-only handler is the final application-level security
         * boundary before outbound Acronis traffic reaches the network.
         *
         * It permits only:
         * - OAuth token acquisition
         * - explicitly approved GET endpoints
         *
         * Everything else fails closed.
         */
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
                acronisOptions
            );

        provider =
            new HttpAcronisProvider(
                apiClient
            );

        timeProvider =
            TimeProvider.System;

        /*
         * Temporary validation threshold.
         *
         * This should be replaced by plan-aware scheduling logic once real
         * production data has been inspected.
         */
        staleAfter =
            TimeSpan.FromHours(48);
    }

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

    var exceptions =
        exceptionEngine.Evaluate(
            snapshot.Resources
        );

    reporter.Write(
        snapshot.Resources,
        exceptions,
        snapshot.Diagnostics
    );
}
catch (Exception exception)
{
    Console.Error.WriteLine();

    Console.Error.WriteLine(
        $"ERROR: {ConsoleErrorFormatter.Format(exception)}"
    );

    /*
     * Stack traces remain available during development without exposing
     * implementation details during normal operator use.
     */
    var debugMode =
        Environment.GetEnvironmentVariable(
            "SUPPORTTOOLKIT_DEBUG"
        );

    if (debugMode?.Equals(
            "true",
            StringComparison.OrdinalIgnoreCase) == true)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(exception);
    }

    Environment.ExitCode = 1;
}

sealed class FixedTimeProvider : TimeProvider
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