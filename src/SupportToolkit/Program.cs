using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Reporting;

Console.WriteLine("SupportToolkit");
Console.WriteLine("Running Backup Health in fixture mode...");

var fixtureDirectory = Path.Combine(
    AppContext.BaseDirectory,
    "Fixtures",
    "Acronis"
);

IAcronisProvider provider =
    new FixtureAcronisProvider(
        fixtureDirectory
    );

var backupHealthService =
    new BackupHealthService(
        provider
    );

// Fixture mode uses a fixed clock so our synthetic test
// environment behaves the same regardless of when we run it.
var fixtureNow = new DateTimeOffset(
    2026,
    10,
    1,
    12,
    0,
    0,
    TimeSpan.Zero
);

var exceptionEngine =
    new BackupExceptionEngine(
        new FixedTimeProvider(fixtureNow),
        TimeSpan.FromHours(48)
    );

var reporter =
    new ConsoleBackupHealthReporter();

var resources =
    await backupHealthService
        .GetBackupResourcesAsync();

var exceptions =
    exceptionEngine.Evaluate(resources);

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
