namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Non-secret destination identity shown before an interactive ticket write.
/// DestinationName is a SupportToolkit alias, not the ticketing system's
/// own group name.
/// </summary>
public sealed record TicketDestinationPreview(
    string InstanceHost,
    string DestinationName,
    long GroupId)
{
    public bool IsSameAs(TicketDestinationPreview other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return GroupId == other.GroupId
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
