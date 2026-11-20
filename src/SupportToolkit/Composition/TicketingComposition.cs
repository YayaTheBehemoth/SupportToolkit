using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.OsTicket.Tickets;
using SupportToolkit.Providers.Zendesk.Tickets;

namespace SupportToolkit.Composition;

/// <summary>
/// Chooses the ticketing system the active profile selects. This is the
/// only place that maps a ticketing system name to its provider; the
/// selection is read on every call, so a profile switched while the
/// program runs takes effect on the next ticket.
/// </summary>
public static class TicketingComposition
{
    public static async Task<ITicketingTarget> ResolveTargetAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var selection =
            await resolver.GetTicketingSelectionAsync(cancellationToken);

        return selection?.Provider switch
        {
            null =>
                throw new InvalidOperationException(
                    "The active profile has no ticket destination. " +
                    "Configure one with SupportToolkit configure profile " +
                    "<name>, or select one in the profile's ticketing " +
                    "setting."),

            TicketingSelection.Providers.Zendesk =>
                await ZendeskTicketingTarget.ResolveAsync(
                    resolver,
                    cancellationToken),

            TicketingSelection.Providers.OsTicket =>
                await OsTicketTicketingTarget.ResolveAsync(
                    resolver,
                    cancellationToken),

            "" =>
                throw new InvalidOperationException(
                    "The active profile's ticketing setting names no " +
                    "provider. Use one of: " +
                    $"{string.Join(", ", KnownProviders)}."),

            var unknown =>
                throw new InvalidOperationException(
                    $"The active profile selects ticketing provider " +
                    $"'{unknown}', which SupportToolkit does not know. " +
                    $"Use one of: {string.Join(", ", KnownProviders)}.")
        };
    }

    public static IReadOnlyList<string> KnownProviders { get; } =
    [
        TicketingSelection.Providers.Zendesk,
        TicketingSelection.Providers.OsTicket
    ];
}
