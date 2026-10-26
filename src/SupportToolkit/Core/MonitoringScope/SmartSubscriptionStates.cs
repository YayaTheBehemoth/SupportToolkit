namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Interpreted "blocked" evidence of a subscription header.
///
/// NotBlocked is the AL blank member, which Business Central serializes
/// as "_x0020_" and which decodes to a space. NotReported means the
/// source supplied no value; it is missing evidence, never "not blocked".
/// </summary>
public enum SubscriptionBlockedState
{
    NotBlocked,
    All,
    Invoice,
    Unrecognized,
    NotReported
}

/// <summary>
/// Interpreted status of a subscription header.
///
/// Open is recognized but its business meaning is unverified.
/// </summary>
public enum SubscriptionStatusState
{
    Active,
    Suspended,
    Open,
    Unrecognized
}

/// <summary>
/// Parses decoded header values into explicit states.
///
/// Values must already be decoded from their XML-encoded AL names.
/// Anything that is not an exact known member is Unrecognized rather
/// than guessed.
/// </summary>
public static class SmartSubscriptionStates
{
    public static SubscriptionBlockedState ParseBlocked(
        string? decodedValue)
    {
        if (decodedValue is null)
        {
            return SubscriptionBlockedState.NotReported;
        }

        var value =
            decodedValue.Trim();

        // The blank AL member decodes to whitespace. "false" covers a
        // boolean representation, matching the entitlement evaluator.
        if (value.Length == 0
            || string.Equals(
                value,
                "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionBlockedState.NotBlocked;
        }

        if (string.Equals(
            value,
            "All",
            StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionBlockedState.All;
        }

        if (string.Equals(
            value,
            "Invoice",
            StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionBlockedState.Invoice;
        }

        return SubscriptionBlockedState.Unrecognized;
    }

    public static SubscriptionStatusState ParseStatus(
        string? value,
        SmartSubscriptionStatusVocabulary? vocabulary = null)
    {
        vocabulary ??= SmartSubscriptionStatusVocabulary.English;

        var status =
            value?.Trim();

        if (string.IsNullOrEmpty(status))
        {
            return SubscriptionStatusState.Unrecognized;
        }

        if (string.Equals(
            status,
            vocabulary.Active,
            StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionStatusState.Active;
        }

        if (string.Equals(
            status,
            vocabulary.Suspended,
            StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionStatusState.Suspended;
        }

        if (vocabulary.Open is not null
            && string.Equals(
                status,
                vocabulary.Open,
                StringComparison.OrdinalIgnoreCase))
        {
            return SubscriptionStatusState.Open;
        }

        return SubscriptionStatusState.Unrecognized;
    }
}
