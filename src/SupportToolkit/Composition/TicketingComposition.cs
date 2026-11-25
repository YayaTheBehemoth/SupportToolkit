using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.OsTicket.Configuration;
using SupportToolkit.Providers.OsTicket.Tickets;
using SupportToolkit.Providers.Zendesk.Tickets;

namespace SupportToolkit.Composition;

/// <summary>
/// Chooses the ticketing system the active profile selects. This is the
/// only place that maps a ticketing system name to its provider; adding a
/// system is one entry in Systems. The selection is read on every call,
/// so a profile switched while the program runs takes effect on the next
/// ticket.
/// </summary>
public static class TicketingComposition
{
    private sealed record TicketingSystem(
        string Key,
        string Name,
        Func<SupportToolkitConfigurationResolver, CancellationToken, Task<ITicketingTarget>> Resolve);

    private static readonly TicketingSystem[] Systems =
    [
        new(
            TicketingSelection.Providers.Zendesk,
            "Zendesk",
            async (resolver, token) =>
                await ZendeskTicketingTarget.ResolveAsync(resolver, token)),
        new(
            OsTicketConnections.ProviderKey,
            OsTicketConnections.Instance.ProviderName,
            async (resolver, token) =>
                await OsTicketTicketingTarget.ResolveAsync(resolver, token))
    ];

    public static IReadOnlyList<string> KnownProviders { get; } =
        Systems.Select(system => system.Key).ToArray();

    /// <summary>
    /// The selected system's name for display ("osTicket"), or the
    /// configured value when no such system is registered.
    /// </summary>
    public static string SystemName(TicketingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return Find(selection)?.Name
            ?? (selection.Provider.Length == 0 ? "Ticketing" : selection.Provider);
    }

    public static async Task<ITicketingTarget> ResolveTargetAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var selection =
            await resolver.GetTicketingSelectionAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The active profile has no ticket destination. " +
                "Configure one with SupportToolkit configure profile " +
                "<name>, or select one in the profile's ticketing " +
                "setting.");

        if (selection.Provider.Length == 0)
        {
            throw new InvalidOperationException(
                "The active profile's ticketing setting names no " +
                $"provider. Use one of: {string.Join(", ", KnownProviders)}.");
        }

        var system =
            Find(selection)
            ?? throw new InvalidOperationException(
                $"The active profile selects ticketing provider " +
                $"'{selection.Provider}', which SupportToolkit does not know. " +
                $"Use one of: {string.Join(", ", KnownProviders)}.");

        return await system.Resolve(resolver, cancellationToken);
    }

    private static TicketingSystem? Find(TicketingSelection selection) =>
        Systems.FirstOrDefault(system => selection.Is(system.Key));
}
