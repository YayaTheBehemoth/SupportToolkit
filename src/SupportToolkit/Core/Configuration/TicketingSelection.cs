namespace SupportToolkit.Core.Configuration;

/// <summary>
/// The ticketing system, connection and destination a profile sends
/// tickets to, read the same way everywhere.
///
/// The profile's Ticketing block wins when present. Without it, the older
/// ZendeskConnection and ZendeskDestination pair selects Zendesk, so
/// configurations saved before the block existed keep working unchanged.
/// Values are as configured; resolution validates them.
/// </summary>
public sealed record TicketingSelection(
    string Provider,
    string? Connection,
    string? Destination)
{
    /// <summary>
    /// The ticketing system names Core itself needs: Zendesk, which the
    /// older profile fields select. The others are registered in
    /// Composition/.
    /// </summary>
    public static class Providers
    {
        public const string Zendesk = "zendesk";
    }

    /// <summary>
    /// The profile's selection, or null when it sends tickets nowhere.
    /// A Ticketing block without a provider yields an empty Provider,
    /// which resolution reports instead of guessing.
    /// </summary>
    public static TicketingSelection? For(
        SupportToolkitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Ticketing is { } block)
        {
            return new TicketingSelection(
                block.Provider?.Trim().ToLowerInvariant() ?? string.Empty,
                Blank(block.Connection),
                Blank(block.Destination));
        }

        if (!string.IsNullOrWhiteSpace(profile.ZendeskConnection))
        {
            return new TicketingSelection(
                Providers.Zendesk,
                profile.ZendeskConnection,
                Blank(profile.ZendeskDestination));
        }

        return null;
    }

    /// <summary>
    /// For editors that know only the ZendeskConnection and
    /// ZendeskDestination fields: a Ticketing block that selects Zendesk
    /// is moved into them, so the two can never disagree. Returns the
    /// selection when the block selects another system (left unchanged),
    /// otherwise null.
    /// </summary>
    public static TicketingSelection? MoveZendeskBlockToZendeskFields(
        SupportToolkitProfile profile)
    {
        var selection = For(profile);

        if (profile.Ticketing is null || selection is null)
        {
            return null;
        }

        if (!selection.Is(Providers.Zendesk))
        {
            return selection;
        }

        profile.ZendeskConnection = selection.Connection;
        profile.ZendeskDestination = selection.Destination;
        profile.Ticketing = null;

        return null;
    }

    public bool Is(string provider) =>
        string.Equals(Provider, provider, StringComparison.Ordinal);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
