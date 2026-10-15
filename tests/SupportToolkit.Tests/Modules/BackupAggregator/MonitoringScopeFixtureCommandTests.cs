
using System.Globalization;
using SupportToolkit.Cli.Commands;

namespace SupportToolkit.Tests;

public class MonitoringScopeFixtureCommandTests
{
    private static readonly DateOnly EvaluationDate =
        new(2026, 10, 15);

    [Fact]
    public void Run_ProducesMostlyActiveCandidates()
    {
        using var writer =
            new StringWriter(
                CultureInfo.InvariantCulture
            );

        var exitCode =
            new MonitoringScopeFixtureCommand()
                .Run(
                    writer,
                    EvaluationDate
                );

        var report =
            writer.ToString();

        Assert.Equal(
            0,
            exitCode
        );

        Assert.Contains(
            "Source: Synthetic SMARTsubscription records",
            report
        );

        Assert.Contains(
            "Monitoring item: MONITORING-SERVICE",
            report
        );

        Assert.Contains(
            "Subscription headers: 5",
            report
        );

        Assert.Contains(
            "Subscription lines: 6",
            report
        );

        Assert.Contains(
            "Subscriptions with monitoring evidence: 4",
            report
        );

        Assert.Contains(
            "Active candidates: 3",
            report
        );

        Assert.Contains(
            "Needs review: 1",
            report
        );

        Assert.Contains(
            "Headers without monitoring line: 1",
            report
        );
    }

    [Fact]
    public void Run_DisplaysExpectedFixtureScenarios()
    {
        using var writer =
            new StringWriter(
                CultureInfo.InvariantCulture
            );

        new MonitoringScopeFixtureCommand()
            .Run(
                writer,
                EvaluationDate
            );

        var report =
            writer.ToString();

        Assert.Contains(
            "SUB-NORTHWIND",
            report
        );

        Assert.Contains(
            "SUB-FABRIKAM",
            report
        );

        Assert.Contains(
            "SUB-ADVENTURE",
            report
        );

        Assert.Contains(
            "SUB-CONTOSO",
            report
        );

        Assert.Contains(
            "SUB-ALPINE",
            report
        );

        Assert.Contains(
            "monitoring responsibility is unverified",
            report
        );

        Assert.DoesNotContain(
            "SUB-ORPHAN",
            report
        );
    }

    [Fact]
    public void Run_MissingMonitoringEvidenceIsNotNonEntitlement()
    {
        using var writer =
            new StringWriter(
                CultureInfo.InvariantCulture
            );

        new MonitoringScopeFixtureCommand()
            .Run(
                writer,
                EvaluationDate
            );

        var report =
            writer.ToString();

        Assert.Contains(
            "HEADERS WITHOUT MONITORING EVIDENCE",
            report
        );

        Assert.Contains(
            "Missing monitoring lines do not establish non-entitlement.",
            report
        );

        Assert.Contains(
            "No BC-to-Acronis identity reconciliation has been performed.",
            report
        );
    }

    [Fact]
    public void Run_NullOutput_Throws()
    {
        var command =
            new MonitoringScopeFixtureCommand();

        Assert.Throws<ArgumentNullException>(
            () => command.Run(
                null!,
                EvaluationDate
            )
        );
    }
}
