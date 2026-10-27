using SupportToolkit.Core.MonitoringScope;

namespace SupportToolkit.Tests;

public class CustomerEntitlementActivityTests
{
    private const string Item = "ITEM-1";
    private static readonly DateOnly AsOf = new(2026, 10, 15);

    private readonly SmartSubscriptionEntitlementEvaluator _evaluator =
        new(Item);

    [Fact]
    public void Customer_WithOnlySuspendedSubscriptions_IsInactive()
    {
        var state =
            StateOf(
                [Header("SUB-1", "BC-1", "Suspended")],
                [Line("SUB-1")]);

        Assert.Equal(CustomerEntitlementState.Inactive, state);
    }

    [Fact]
    public void Customer_WithOnlyBlockedAllSubscriptions_IsInactive()
    {
        var state =
            StateOf(
                [Header("SUB-1", "BC-1", "Active", "All")],
                [Line("SUB-1")]);

        Assert.Equal(CustomerEntitlementState.Inactive, state);
    }

    [Fact]
    public void ActiveSubscription_MakesTheCustomerActive_EvenWithSuspendedSiblings()
    {
        var state =
            StateOf(
                [
                    Header("SUB-1", "BC-1", "Active"),
                    Header("SUB-2", "BC-1", "Suspended")
                ],
                [Line("SUB-1"), Line("SUB-2")]);

        Assert.Equal(CustomerEntitlementState.Active, state);
    }

    [Theory]
    [InlineData("Open", " ")]
    [InlineData("Active", "Invoice")]
    [InlineData("Active", "Unknown value")]
    public void UnclearEvidence_IsNeverTreatedAsInactive(
        string status,
        string blocked)
    {
        var state =
            StateOf(
                [Header("SUB-1", "BC-1", status, blocked)],
                [Line("SUB-1")]);

        Assert.Equal(CustomerEntitlementState.NeedsReview, state);
    }

    [Fact]
    public void SuspendedPlusAnUnclearSibling_NeedsReview()
    {
        var state =
            StateOf(
                [
                    Header("SUB-1", "BC-1", "Suspended"),
                    Header("SUB-2", "BC-1", "Open")
                ],
                [Line("SUB-1"), Line("SUB-2")]);

        Assert.Equal(CustomerEntitlementState.NeedsReview, state);
    }

    private CustomerEntitlementState StateOf(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines)
    {
        var observations =
            _evaluator.Evaluate(headers, lines, AsOf);

        return CustomerEntitlementActivity.Classify(observations)["BC-1"];
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status,
        string blocked = " ")
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            null,
            blocked);
    }

    private static SmartSubscriptionLine Line(
        string subscription)
    {
        return new SmartSubscriptionLine(
            subscription,
            Item,
            "Item",
            false);
    }
}
