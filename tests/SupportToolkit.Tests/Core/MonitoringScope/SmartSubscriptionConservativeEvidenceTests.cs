using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Tests;

/// <summary>
/// Evidence whose business meaning is unverified is preserved and
/// classified conservatively, never interpreted as exclusion or
/// entitlement.
/// </summary>
public class SmartSubscriptionConservativeEvidenceTests
{
    private const string Monitoring =
        SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber;

    private static readonly DateOnly Today =
        new(2026, 10, 15);

    private readonly SmartSubscriptionEntitlementEvaluator _evaluator =
        new(Monitoring);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("false")]
    [InlineData("FALSE")]
    public void NotBlocked_ActiveSubscription_IsCandidate(
        string blocked)
    {
        var result = Evaluate(Header(blocked: blocked), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("All")]
    [InlineData("Yes")]
    [InlineData("3")]
    public void BlockedOrUnrecognized_RequiresReview_EvenWhenStatusActive(
        string blocked)
    {
        var result = Evaluate(Header(blocked: blocked), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("blocked", result.Reason);

        // Preserved as evidence for the customer, not excluded.
        Assert.Equal("BC-1", result.CustomerNumber);
    }

    [Fact]
    public void MissingBlockedEvidence_RequiresReview()
    {
        var result =
            Evaluate(Header(blocked: null), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("does not report", result.Reason);
    }

    [Fact]
    public void DifferentSellToAndBillTo_RequiresReview()
    {
        var result =
            Evaluate(Header(billTo: "BC-OTHER"), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("differ", result.Reason);

        // Sell-to remains the provisional customer.
        Assert.Equal("BC-1", result.CustomerNumber);
    }

    [Theory]
    [InlineData("bc-1")]
    [InlineData(" BC-1 ")]
    [InlineData(null)]
    public void MatchingOrUnreportedBillTo_IsNotFlagged(
        string? billTo)
    {
        var result =
            Evaluate(Header(billTo: billTo), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Theory]
    [InlineData("Comment")]
    [InlineData("G/L Account")]
    [InlineData("")]
    [InlineData(null)]
    public void MonitoringNumberWithoutConfirmedItemType_RequiresReview(
        string? lineType)
    {
        var result =
            Evaluate(Header(), Line(lineType: lineType));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("confirmed item line", result.Reason);
    }

    [Fact]
    public void ItemTypeMatching_IsCaseInsensitive()
    {
        var result =
            Evaluate(Header(), Line(lineType: "item"));

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Fact]
    public void UnconfirmedTypeLine_IsReviewedEvenAlongsideConfirmedLine()
    {
        var result =
            Evaluate(
                Header(),
                Line(),
                Line(lineType: "Comment"));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);
    }

    [Fact]
    public void NonMonitoringLines_AreNotAffectedByTypeRules()
    {
        var observations =
            _evaluator.Evaluate(
                [Header()],
                [new("SUB-1", "OTHER-ITEM", "Comment", true)],
                Today);

        Assert.Empty(observations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void OmittedOrUnreportedOmitFlag_RequiresReview(
        bool? omit)
    {
        var result =
            Evaluate(Header(), Line(omit: omit));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("omit", result.Reason);
    }

    [Fact]
    public void UnknownStatus_IsReportedExplicitly()
    {
        var result =
            Evaluate(Header(status: "Aktiv"), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("'Aktiv'", result.Reason);
    }

    [Fact]
    public void UnknownStatus_ReportIsBoundedAndSanitized()
    {
        var hostile =
            "X\r\nINJECT" + new string('y', 200);

        var result =
            Evaluate(Header(status: hostile), Line());

        Assert.DoesNotContain('\r', result.Reason);
        Assert.DoesNotContain('\n', result.Reason);
        Assert.True(result.Reason.Length < 150);
    }

    [Theory]
    [InlineData("Aktiv")]
    [InlineData("Spærret")]
    [InlineData("Open")]
    [InlineData("Planlægges")]
    public void StatusOutsideTheVocabulary_IsReportedNotInterpreted(
        string status)
    {
        var result =
            Evaluate(Header(status: status), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains($"'{status}'", result.Reason);
        Assert.Contains("not recognized", result.Reason);
    }

    [Fact]
    public void Suspended_IsReviewedWithSpecificReason()
    {
        var result =
            Evaluate(Header(status: "suspended"), Line());

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("suspended", result.Reason);
    }

    [Fact]
    public void CustomVocabulary_IsHonored()
    {
        var evaluator =
            new SmartSubscriptionEntitlementEvaluator(
                Monitoring,
                vocabulary:
                    new SmartSubscriptionStatusVocabulary(
                        "Aktiv",
                        "Spærret"));

        var result =
            Assert.Single(
                evaluator.Evaluate(
                    [Header(status: "Aktiv")],
                    [Line()],
                    Today));

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Fact]
    public void EmptyStatus_IsReportedAsEmpty()
    {
        var result =
            Evaluate(Header(status: ""), Line());

        Assert.Contains("(empty)", result.Reason);
    }

    [Fact]
    public void UnknownStatus_PreventsCompleteScope()
    {
        var (tenants, identities) =
            MonitoringTenantSelectionFixtureFactory.Create();

        var observations =
            _evaluator.Evaluate(
                [
                    Header(
                        number: "SUB-1",
                        customer: "BC-NORTHWIND",
                        status: "Aktiv")
                ],
                [Line()],
                Today);

        var selection =
            new MonitoringTenantSelector()
                .Select(tenants, observations, identities);

        Assert.Contains(
            selection.Tenants,
            finding =>
                finding.Tenant.Name == "Northwind Manufacturing"
                && finding.Status
                    == MonitoringTenantSelectionStatus.NeedsReview);

        Assert.True(selection.NeedsReviewCount > 0);
        Assert.DoesNotContain(
            selection.InventoryCandidates,
            tenant => tenant.Name == "Northwind Manufacturing");
    }

    private MonitoringEntitlementObservation Evaluate(
        SmartSubscriptionHeader header,
        params SmartSubscriptionLine[] lines)
    {
        return Assert.Single(
            _evaluator.Evaluate([header], lines, Today));
    }

    private static SmartSubscriptionHeader Header(
        string number = "SUB-1",
        string customer = "BC-1",
        string status = "Active",
        string? billTo = "BC-1",
        string? blocked = "")
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            billTo,
            blocked);
    }

    private static SmartSubscriptionLine Line(
        string? lineType = "Item",
        bool? omit = false)
    {
        return new SmartSubscriptionLine(
            "SUB-1",
            Monitoring,
            lineType,
            omit);
    }
}
