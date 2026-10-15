
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Tests;

public class SmartSubscriptionEntitlementEvaluatorTests
{
    private static readonly DateOnly EvaluationDate =
        new(2026, 10, 15);

    private readonly SmartSubscriptionEntitlementEvaluator
        _evaluator = new();

    [Fact]
    public void ActiveMonitoringSubscription_IsCandidate()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        var northwind =
            Assert.Single(
                results,
                result =>
                    result.SubscriptionNumber
                    == "SUB-NORTHWIND"
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            northwind.Assessment
        );

        Assert.Equal(
            "BC-NORTHWIND",
            northwind.CustomerNumber
        );
    }

    [Fact]
    public void BlockedSubscription_RequiresReview_EvenWithoutEndDate()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        var contoso =
            Assert.Single(
                results,
                result =>
                    result.SubscriptionNumber
                    == "SUB-CONTOSO"
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            contoso.Assessment
        );

        Assert.Contains(
            "blocked",
            contoso.Reason,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void BackupServiceWithoutMonitoringItem_ProducesNoEntitlement()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        Assert.DoesNotContain(
            results,
            result =>
                result.SubscriptionNumber == "SUB-ALPINE"
        );
    }

    [Fact]
    public void PlannedMonitoringSubscription_RequiresReview()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        var fabrikam =
            Assert.Single(
                results,
                result =>
                    result.SubscriptionNumber
                    == "SUB-FABRIKAM"
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            fabrikam.Assessment
        );
    }

    [Fact]
    public void ExpiredMonitoringSubscription_RequiresReview()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        var adventureWorks =
            Assert.Single(
                results,
                result =>
                    result.SubscriptionNumber
                    == "SUB-ADVENTURE"
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            adventureWorks.Assessment
        );

        Assert.Contains(
            "end date has passed",
            adventureWorks.Reason
        );
    }

    [Fact]
    public void MissingSubscriptionHeader_RemainsVisible()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        var orphan =
            Assert.Single(
                results,
                result =>
                    result.SubscriptionNumber
                    == "SUB-ORPHAN"
            );

        Assert.Null(
            orphan.CustomerNumber
        );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            orphan.Assessment
        );
    }

    [Fact]
    public void MissingCustomerReference_RequiresReview()
    {
        var results =
            EvaluateSingle(
                customerNumber: null
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void ActiveSubscriptionWithoutStartDate_RequiresReview()
    {
        var results =
            EvaluateSingle(
                startDate: null
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void ActiveSubscriptionWithFutureStart_RequiresReview()
    {
        var results =
            EvaluateSingle(
                startDate: new DateOnly(2026, 12, 1)
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void UnknownStatus_RequiresReview()
    {
        var results =
            EvaluateSingle(
                status: "Unrecognized"
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void MultipleMonitoringLines_RequireReview()
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            new(
                "SUB-001",
                "BC-001",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new("SUB-001", "BAASOVERVÅG"),
            new("SUB-001", "BAASOVERVÅG")
        ];

        var results =
            _evaluator.Evaluate(
                headers,
                lines,
                EvaluationDate
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void DuplicateSubscriptionHeaders_RequireReview()
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            new(
                "SUB-001",
                "BC-001",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            ),

            new(
                "SUB-001",
                "BC-002",
                "Aktiv",
                new DateOnly(2026, 1, 1),
                null
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new("SUB-001", "BAASOVERVÅG")
        ];

        var results =
            _evaluator.Evaluate(
                headers,
                lines,
                EvaluationDate
            );

        var result =
            Assert.Single(results);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );

        Assert.Null(
            result.CustomerNumber
        );
    }

    private IReadOnlyList<MonitoringEntitlementObservation>
        EvaluateSingle(
            string status = "Aktiv",
            DateOnly? startDate = null,
            string? customerNumber = "BC-001")
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            new(
                "SUB-001",
                customerNumber,
                status,
                startDate,
                null
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-001",
                "BAASOVERVÅG"
            )
        ];

        return _evaluator.Evaluate(
            headers,
            lines,
            EvaluationDate
        );
    }
}
