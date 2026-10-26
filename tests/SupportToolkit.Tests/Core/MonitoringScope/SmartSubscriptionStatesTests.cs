using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Tests;

public class SmartSubscriptionStatesTests
{
    [Theory]
    [InlineData("_x0020_", SubscriptionBlockedState.NotBlocked)]
    [InlineData("All", SubscriptionBlockedState.All)]
    [InlineData("Invoice", SubscriptionBlockedState.Invoice)]
    [InlineData("false", SubscriptionBlockedState.NotBlocked)]
    [InlineData("true", SubscriptionBlockedState.Unrecognized)]
    [InlineData("Ship", SubscriptionBlockedState.Unrecognized)]
    public void BlockedWireValues_ParseAfterDecoding(
        string wire,
        SubscriptionBlockedState expected)
    {
        Assert.Equal(
            expected,
            SmartSubscriptionStates.ParseBlocked(
                BusinessCentralEnumNames.Decode(wire)
            )
        );
    }

    [Fact]
    public void MissingBlocked_IsNotReportedRatherThanNotBlocked()
    {
        Assert.Equal(
            SubscriptionBlockedState.NotReported,
            SmartSubscriptionStates.ParseBlocked(null)
        );
    }

    [Theory]
    [InlineData("Active", SubscriptionStatusState.Active)]
    [InlineData(" suspended ", SubscriptionStatusState.Suspended)]
    [InlineData("Open", SubscriptionStatusState.Open)]
    [InlineData("Closed", SubscriptionStatusState.Unrecognized)]
    [InlineData("", SubscriptionStatusState.Unrecognized)]
    [InlineData(null, SubscriptionStatusState.Unrecognized)]
    public void Status_ParsesOnlyVocabularyMembers(
        string? value,
        SubscriptionStatusState expected)
    {
        Assert.Equal(
            expected,
            SmartSubscriptionStates.ParseStatus(value)
        );
    }

    [Fact]
    public void Status_WithoutOpenInVocabulary_IsUnrecognized()
    {
        Assert.Equal(
            SubscriptionStatusState.Unrecognized,
            SmartSubscriptionStates.ParseStatus(
                "Open",
                new SmartSubscriptionStatusVocabulary("Active", "Suspended")
            )
        );
    }
}
