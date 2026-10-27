using System.Globalization;

using SupportToolkit.Cli.Commands;

namespace SupportToolkit.Tests;

public class CustomerMappingFixtureCommandTests
{
    [Fact]
    public async Task RunAsync_WalksThroughMappingAndShadowReconciliation()
    {
        using var writer =
            new StringWriter(
                CultureInfo.InvariantCulture
            );

        var exitCode =
            await new CustomerMappingFixtureCommand()
                .RunAsync(
                    writer,
                    new DateOnly(2026, 10, 15)
                );

        var report =
            writer.ToString();

        Assert.Equal(0, exitCode);

        // Acronis tenants carry name only, so other keys cannot match.
        Assert.Contains("    Acronis: records 5 | ok 0 | empty 5", report);
        Assert.Contains("-> BC-NORTHWIND | Name | score 0.5", report);
        Assert.Contains("-> BC-ALPINE | Name | score 0.5", report);
        Assert.Contains("-> BC-FABRIKAM | Name | score 0.5", report);
        Assert.DoesNotContain("-> BC-CONTOSO", report);
        Assert.Contains("OneToMany on Name", report);

        Assert.Contains("  Confirmed: 3", report);
        Assert.Contains("  Rejected: 1", report);
        Assert.Contains("  New suggestions on rerun: 0", report);

        // Summary partitions the five synthetic tenants exactly once.
        Assert.Contains("MONITORING SCOPE REPORT", report);
        Assert.Contains("Acronis customer tenants:  5", report);
        Assert.Contains("  In scope this run:       1", report);
        Assert.Contains("  Excluded:                1", report);
        Assert.Contains("  Needs scope review:      1", report);
        Assert.Contains("  Unscoped (no mapping):   2", report);
        Assert.Contains("Discrepancies (open):      0", report);
        Assert.Contains("Acknowledged:              1", report);
        Assert.Contains("Ambiguous:                 1", report);
        Assert.Contains("Unscoped BC records:       2", report);

        Assert.Contains(
            "  Northwind Manufacturing | BC-NORTHWIND | " +
            "SUB-NORTHWIND (MONITORING-SERVICE)",
            report);

        // The known data error is acknowledged, not raised.
        Assert.Contains(
            "  Contoso Workshop | BC-CONTOSO | Not entitled. Entitling " +
            "item only on suspended subscriptions. | " +
            "TenantActiveSubscriptionInactive acknowledged until 2026-11-14",
            report);

        Assert.Contains(
            "  Contoso Workshop | TenantActiveSubscriptionInactive | " +
            "acknowledged until 2026-11-14",
            report);

        Assert.True(
            report.IndexOf("SUMMARY", StringComparison.Ordinal)
            < report.IndexOf("IN SCOPE THIS RUN", StringComparison.Ordinal)
            && report.IndexOf("IN SCOPE THIS RUN", StringComparison.Ordinal)
            < report.IndexOf("EXCLUDED (", StringComparison.Ordinal)
            && report.IndexOf("EXCLUDED (", StringComparison.Ordinal)
            < report.IndexOf("DISCREPANCIES (", StringComparison.Ordinal));

        Assert.Contains(
            "Discrepancy types are provisional; no tickets are created.",
            report
        );
    }
}
