
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Synthetic SMARTsubscription discovery scenarios.
///
/// Customer labels correspond conceptually to existing
/// BackupAggregator fixture tenants.
///
/// No live Business Central data or real customer identifiers.
/// No BC-to-Acronis identity mapping is implied.
/// </summary>
public static class SmartSubscriptionFixtureFactory
{
    public static (
        IReadOnlyList<SmartSubscriptionHeader> Headers,
        IReadOnlyList<SmartSubscriptionLine> Lines
    ) Create()
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            // Northwind: active monitoring.
            new(
                "SUB-NORTHWIND",
                "BC-NORTHWIND",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            // Contoso: monitoring line remains after blocking.
            new(
                "SUB-CONTOSO",
                "BC-CONTOSO",
                "Spærret",
                new DateOnly(2025, 6, 1),
                null
            ),

            // Alpine: backup service, but no monitoring line.
            new(
                "SUB-ALPINE",
                "BC-ALPINE",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            // Fabrikam: future planned monitoring.
            new(
                "SUB-FABRIKAM",
                "BC-FABRIKAM",
                "Planlægges",
                new DateOnly(2026, 11, 1),
                null
            ),

            // Adventure Works: expired date despite active status.
            new(
                "SUB-ADVENTURE",
                "BC-ADVENTURE",
                "Aktiv",
                new DateOnly(2025, 1, 1),
                new DateOnly(2026, 9, 30)
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-NORTHWIND",
                "BAASOVERVÅG"
            ),

            new(
                "SUB-NORTHWIND",
                "BAAS"
            ),

            new(
                "SUB-CONTOSO",
                "BAASOVERVÅG"
            ),

            new(
                "SUB-ALPINE",
                "BAAS"
            ),

            new(
                "SUB-FABRIKAM",
                "BAASOVERVÅG"
            ),

            new(
                "SUB-ADVENTURE",
                "BAASOVERVÅG"
            ),

            // Monitoring line with an unavailable header.
            new(
                "SUB-ORPHAN",
                "BAASOVERVÅG"
            )
        ];

        return (
            headers,
            lines
        );
    }
}
