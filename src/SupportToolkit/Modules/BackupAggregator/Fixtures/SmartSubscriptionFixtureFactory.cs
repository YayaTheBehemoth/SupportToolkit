
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Synthetic SMARTsubscription records for normal development output.
///
/// These fictional customers correspond conceptually to existing
/// BackupAggregator fixtures but do not establish identity mappings.
///
/// Exceptional data-quality scenarios are covered by focused tests.
/// </summary>
public static class SmartSubscriptionFixtureFactory
{
    public const string FixtureMonitoringItemNumber =
        "MONITORING-SERVICE";

    private const string FixtureBackupItemNumber =
        "BACKUP-SERVICE";

    public static (
        IReadOnlyList<SmartSubscriptionHeader> Headers,
        IReadOnlyList<SmartSubscriptionLine> Lines
    ) Create()
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            // Normal monitoring entitlement candidate.
            new(
                "SUB-NORTHWIND",
                "BC-NORTHWIND",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            // Operational exception: blocked subscription.
            new(
                "SUB-CONTOSO",
                "BC-CONTOSO",
                "Spærret",
                new DateOnly(2025, 6, 1),
                null
            ),

            // Backup service exists, but monitoring is not evidenced.
            new(
                "SUB-ALPINE",
                "BC-ALPINE",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            // Normal monitoring entitlement candidate.
            new(
                "SUB-FABRIKAM",
                "BC-FABRIKAM",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            // Normal monitoring entitlement candidate.
            new(
                "SUB-ADVENTURE",
                "BC-ADVENTURE",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-NORTHWIND",
                FixtureMonitoringItemNumber
            ),

            new(
                "SUB-NORTHWIND",
                FixtureBackupItemNumber
            ),

            new(
                "SUB-CONTOSO",
                FixtureMonitoringItemNumber
            ),

            new(
                "SUB-ALPINE",
                FixtureBackupItemNumber
            ),

            new(
                "SUB-FABRIKAM",
                FixtureMonitoringItemNumber
            ),

            new(
                "SUB-ADVENTURE",
                FixtureMonitoringItemNumber
            )
        ];

        return (
            headers,
            lines
        );
    }
}
