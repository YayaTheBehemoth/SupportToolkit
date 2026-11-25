namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Non-secret destination identity shown before an interactive ticket write.
///
/// SystemName is the ticketing system ("Zendesk", "osTicket").
/// DestinationName is a SupportToolkit alias, not the ticketing system's
/// own name for it. RoutingLabel and RoutingId are what the system routes
/// by (for example "Group ID" and "12345"). Caveat, when set, is a limit
/// the operator should know before confirming, such as routing the
/// system does not confirm.
/// </summary>
public sealed record TicketDestinationPreview(
    string SystemName,
    string InstanceHost,
    string DestinationName,
    string RoutingLabel,
    string RoutingId)
{
    public string? Caveat { get; init; }

    public bool IsSameAs(TicketDestinationPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(
                SystemName,
                other.SystemName,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                RoutingId,
                other.RoutingId,
                StringComparison.Ordinal)
            && string.Equals(
                InstanceHost,
                other.InstanceHost,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                DestinationName,
                other.DestinationName,
                StringComparison.OrdinalIgnoreCase);
    }
}
