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

        Assert.Contains("Period: 2026-10", report);
        Assert.Contains("  MatchedConsistent: 1", report);
        Assert.Contains("  MatchedDiscrepancy: 1", report);
        Assert.Contains("  Unscoped: 5", report);
        Assert.Contains("  Ambiguous: 1", report);
        Assert.Contains("  TenantActiveSubscriptionInactive: 1", report);

        Assert.Contains(
            "Discrepancy types are provisional; no tickets are created.",
            report
        );
    }
}
