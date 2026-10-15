
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Tests;

public class SmartSubscriptionEntitlementEvaluatorTests
{
    private static readonly DateOnly EvaluationDate =
        new(2026, 10, 15);

    private readonly SmartSubscriptionEntitlementEvaluator
        _evaluator = new(
            SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
        );

    [Fact]
    public void StandardFixture_HasThreeActiveCandidates()
    {
        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var results =
            _evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                EvaluationDate
            );

        Assert.Equal(
            3,
            results.Count(
                result =>
                    result.Assessment
                    == MonitoringEntitlementAssessment.ActiveCandidate
            )
        );

        Assert.Single(
            results,
            result =>
                result.Assessment
                == MonitoringEntitlementAssessment.NeedsReview
        );
    }

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
                    result.SubscriptionNumber == "SUB-NORTHWIND"
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
    public void BlockedSubscription_RequiresReview()
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
                    result.SubscriptionNumber == "SUB-CONTOSO"
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
    public void PlannedSubscription_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    status: "Planlægges",
                    startDate: new DateOnly(2026, 11, 1)
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void ExpiredSubscription_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    startDate: new DateOnly(2025, 1, 1),
                    endDate: new DateOnly(2026, 9, 30)
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );

        Assert.Contains(
            "end date has passed",
            result.Reason
        );
    }

    [Fact]
    public void MissingSubscriptionHeader_RemainsVisible()
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
            [];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-ORPHAN",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            )
        ];

        var result =
            Assert.Single(
                _evaluator.Evaluate(
                    headers,
                    lines,
                    EvaluationDate
                )
            );

        Assert.Null(
            result.CustomerNumber
        );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void MissingCustomerReference_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    startDate: new DateOnly(2026, 1, 1),
                    customerNumber: null
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void MissingStartDate_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle()
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void FutureStartDate_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    startDate: new DateOnly(2026, 12, 1)
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void UnknownStatus_RequiresReview()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    status: "Unrecognized",
                    startDate: new DateOnly(2026, 1, 1)
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );
    }

    [Fact]
    public void StatusMatching_IsCaseInsensitive()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    status: "aktiv",
                    startDate: new DateOnly(2026, 1, 1)
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment
        );
    }

    [Fact]
    public void EndDateOnEvaluationDay_IsStillApplicable()
    {
        var result =
            Assert.Single(
                EvaluateSingle(
                    startDate: new DateOnly(2026, 1, 1),
                    endDate: EvaluationDate
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
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
            new(
                "SUB-001",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            ),
            new(
                "SUB-001",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            )
        ];

        var result =
            Assert.Single(
                _evaluator.Evaluate(
                    headers,
                    lines,
                    EvaluationDate
                )
            );

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
            new(
                "SUB-001",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            )
        ];

        var result =
            Assert.Single(
                _evaluator.Evaluate(
                    headers,
                    lines,
                    EvaluationDate
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment
        );

        Assert.Null(
            result.CustomerNumber
        );
    }

    [Fact]
    public void ItemIdentifier_IsNotHardcodedInEvaluator()
    {
        var customEvaluator =
            new SmartSubscriptionEntitlementEvaluator(
                "CUSTOM-MONITORING-ITEM"
            );

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
            new("SUB-001", "OTHER-ITEM"),
            new("SUB-001", "CUSTOM-MONITORING-ITEM")
        ];

        var result =
            Assert.Single(
                customEvaluator.Evaluate(
                    headers,
                    lines,
                    EvaluationDate
                )
            );

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment
        );
    }

    private IReadOnlyList<MonitoringEntitlementObservation>
        EvaluateSingle(
            string status = "Aktiv",
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            string? customerNumber = "BC-001")
    {
        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            new(
                "SUB-001",
                customerNumber,
                status,
                startDate,
                endDate
            )
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
        [
            new(
                "SUB-001",
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            )
        ];

        return _evaluator.Evaluate(
            headers,
            lines,
            EvaluationDate
        );
    }
}
