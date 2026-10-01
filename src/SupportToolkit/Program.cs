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

// Fixture mode wires the synthetic Acronis provider into the BackupHealth pipeline so the rest of the application
// can evaluate domain results without depending on real credentials, network access, or live Acronis responses.
IAcronisProvider provider =
    new FixtureAcronisProvider(
        fixtureDirectory
    );

var backupHealthService =
    new BackupHealthService(
        provider
    );

// A fixed clock keeps fixture-driven time-based rules deterministic and repeatable for local development and tests.
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
