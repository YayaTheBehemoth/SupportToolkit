using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Reporting;

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

    var httpClient =
        new HttpClient();

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

var resources =
    await backupHealthService
        .GetBackupResourcesAsync();

var exceptions =
    exceptionEngine.Evaluate(
        resources
    );

reporter.Write(
    resources,
    exceptions
);

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