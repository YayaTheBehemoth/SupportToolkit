using SupportToolkit.Core.MonitoringScope;

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
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                "BC-NORTHWIND",
                ""
            ),

            // Operational exception: blocked subscription.
            new(
                "SUB-CONTOSO",
                "BC-CONTOSO",
                "Suspended",
                new DateOnly(2025, 6, 1),
                null,
                "BC-CONTOSO",
                ""
            ),

            // Backup service exists, but monitoring is not evidenced.
            new(
                "SUB-ALPINE",
                "BC-ALPINE",
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                "BC-ALPINE",
                ""
            ),

            // Normal monitoring entitlement candidate.
            new(
                "SUB-FABRIKAM",
                "BC-FABRIKAM",
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                "BC-FABRIKAM",
                ""
            ),

            // Normal monitoring entitlement candidate.
            new(
                "SUB-ADVENTURE",
                "BC-ADVENTURE",
                "Active",
                new DateOnly(2026, 1, 1),
                null,
                "BC-ADVENTURE",
                ""
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-NORTHWIND",
                FixtureMonitoringItemNumber,
                "Item",
                false
            ),

            new(
                "SUB-NORTHWIND",
                FixtureBackupItemNumber,
                "Item",
                false
            ),

            new(
                "SUB-CONTOSO",
                FixtureMonitoringItemNumber,
                "Item",
                false
            ),

            new(
                "SUB-ALPINE",
                FixtureBackupItemNumber,
                "Item",
                false
            ),

            new(
                "SUB-FABRIKAM",
                FixtureMonitoringItemNumber,
                "Item",
                false
            ),

            new(
                "SUB-ADVENTURE",
                FixtureMonitoringItemNumber,
                "Item",
                false
            )
        ];

        return (
            headers,
            lines
        );
    }
}
