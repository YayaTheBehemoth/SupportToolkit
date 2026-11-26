using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BulkDelete.Models;
using SupportToolkit.Modules.BulkDelete.Production;
using SupportToolkit.Modules.BulkDelete.Services;
using SupportToolkit.Providers.Zammad.Configuration;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Composition;

/// <summary>
/// Builds bulk delete for a host. Bulk delete works on the ticketing system
/// the active profile selects (its ticketing setting): a profile whose
/// review tickets go to one system never deletes in another. This is the
/// only place that maps a ticketing system to its deletion; adding one is
/// one entry in Systems.
///
/// Deleting needs its own access, kept apart from the access that creates
/// tickets (for Zendesk, the profile's deletion connection, never the
/// ticketing connection). That access must point at the same instance as
/// the ticketing selection, or bulk delete is refused.
///
/// A ticketing system SupportToolkit cannot delete from is reported as
/// unavailable; it never falls back to another system. A profile that
/// selects no ticketing system keeps using its Zendesk deletion
/// connection, as profiles saved before bulk delete followed the ticketing
/// selection did.
///
/// Everything is resolved on every call, so a profile switched while the
/// program runs is used next time. Opening a session signs in to nothing.
/// </summary>
public static class BulkDeleteComposition
{
    private sealed record DeletionSystem(
        string Key,
        TicketSystemRules Rules,
        Func<SupportToolkitConfigurationResolver, CancellationToken, Task<string>> ResolveInstance,
        Func<SupportToolkitConfigurationResolver, Action<Uri>?, CancellationToken, Task<BulkDeleteConnection>> ConnectWithDeletion);

    private static readonly DeletionSystem[] Systems =
    [
        new(
            TicketingSelection.Providers.Zendesk,
            ZendeskDeletionRules.Rules,
            async (resolver, token) =>
                (await ResolveZendeskAsync(resolver, token)).Subdomain,
            async (resolver, browserUnavailable, token) =>
            {
                var session =
                    ZendeskBulkDeleteSession.Create(
                        await ResolveZendeskAsync(resolver, token),
                        enableDeletion: true,
                        browserUnavailable: browserUnavailable);

                return new BulkDeleteConnection(
                    session.CreateReviewSession(),
                    session)
                {
                    Rules = ZendeskDeletionRules.Rules
                };
            }),
        new(
            ZammadConnections.ProviderKey,
            ZammadDeletionRules.Rules,
            async (resolver, token) =>
            {
                var connection =
                    await ZammadConnections.Instance.ResolveDeletionAsync(resolver, token);

                return ZammadDeletionRules.InstanceOf(
                    ZammadOptions.Create(connection.BaseUrl, connection.DeletionToken).BaseUri);
            },
            async (resolver, _, token) =>
            {
                var session =
                    ZammadBulkDeleteSession.Create(
                        await ZammadConnections.Instance.ResolveDeletionAsync(resolver, token),
                        enableDeletion: true);

                return new BulkDeleteConnection(
                    session.CreateReviewSession(),
                    session)
                {
                    Rules = ZammadDeletionRules.Rules
                };
            })
    ];

    /// <summary>
    /// Local check only: whether the active profile can be used for bulk
    /// deletion, the system and the instance it would delete from.
    /// </summary>
    public static async Task<BulkDeleteAvailability> GetAvailabilityAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        string? systemName = null;

        try
        {
            var (system, name) =
                await SelectAsync(configurationResolver, cancellationToken);

            systemName = name;

            if (system is null)
            {
                return new BulkDeleteAvailability(
                    false,
                    null,
                    $"Bulk delete is not available for {name}: " +
                    $"SupportToolkit cannot delete tickets in {name}.")
                {
                    SystemName = name
                };
            }

            var instance =
                await system.ResolveInstance(
                    configurationResolver,
                    cancellationToken);

            return new BulkDeleteAvailability(true, instance, null)
            {
                Rules = system.Rules,
                SystemName = name
            };
        }
        catch (InvalidOperationException exception)
        {
            return new BulkDeleteAvailability(
                false,
                null,
                exception.Message)
            {
                SystemName = systemName
            };
        }
    }

    /// <summary>
    /// The operator-facing session for the web UI, with deletion enabled:
    /// it deletes only after the operator's typed confirmations.
    /// </summary>
    public static async Task<BulkDeleteConnection> ConnectWithDeletionAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        Action<Uri>? browserUnavailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        var (system, name) =
            await SelectAsync(configurationResolver, cancellationToken);

        if (system is null)
        {
            throw new InvalidOperationException(
                $"Bulk delete is not available for {name}: " +
                $"SupportToolkit cannot delete tickets in {name}.");
        }

        return await system.ConnectWithDeletion(
            configurationResolver,
            browserUnavailable,
            cancellationToken);
    }

    /// <summary>
    /// The Zendesk connection itself, for the console command that deletes
    /// from Zendesk. Refused when the profile's ticketing system is not
    /// Zendesk. Deletion is enabled only when asked; without it the session
    /// can search and preview, never delete.
    /// </summary>
    public static async Task<ZendeskBulkDeleteSession> OpenSessionAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        OperationalLogger? logger = null,
        bool enableDeletion = false,
        string? receiptDirectory = null,
        Action<Uri>? browserUnavailable = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        var (system, name) =
            await SelectAsync(configurationResolver, cancellationToken);

        if (system?.Key != TicketingSelection.Providers.Zendesk)
        {
            throw new InvalidOperationException(
                $"The active profile's ticketing system is {name}. This " +
                "command deletes Zendesk tickets only, and bulk delete " +
                "works on the profile's own ticketing system.");
        }

        return ZendeskBulkDeleteSession.Create(
            await ResolveZendeskAsync(configurationResolver, cancellationToken),
            logger,
            enableDeletion,
            receiptDirectory,
            browserUnavailable);
    }

    /// <summary>
    /// The deletion system for the profile's ticketing selection, or null
    /// when SupportToolkit cannot delete in the selected system, with the
    /// system's name for messages.
    /// </summary>
    private static async Task<(DeletionSystem? System, string Name)> SelectAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var selection =
            await resolver.GetTicketingSelectionAsync(cancellationToken);

        // No ticketing system selected: the Zendesk deletion connection
        // alone decides, as before bulk delete followed the selection.
        var key = selection?.Provider ?? TicketingSelection.Providers.Zendesk;

        var system =
            Systems.FirstOrDefault(
                candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));

        var name =
            system?.Rules.SystemName
            ?? (selection is null ? key : TicketingComposition.SystemName(selection));

        return (system, name);
    }

    /// <summary>
    /// The profile's Zendesk deletion connection, refused when the profile
    /// sends tickets to a different Zendesk instance.
    /// </summary>
    private static async Task<ResolvedZendeskDeletionConnection> ResolveZendeskAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var deletion =
            await resolver.GetZendeskDeletionConnectionAsync(cancellationToken);

        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);
        var selection = TicketingSelection.For(active.Profile);

        if (selection is null
            || !selection.Is(TicketingSelection.Providers.Zendesk)
            || string.IsNullOrWhiteSpace(selection.Connection))
        {
            return deletion;
        }

        var ticketingName =
            SupportToolkitConfigurationNames.NormalizeConnectionName(
                selection.Connection);

        // The saved ticketing connection; a missing one is reported where
        // tickets are created, not here.
        var ticketing =
            active.Configuration.Connections.Zendesk
                .FirstOrDefault(
                    pair => string.Equals(
                        pair.Key,
                        ticketingName,
                        StringComparison.OrdinalIgnoreCase))
                .Value;

        var ticketingSubdomain = ticketing?.Subdomain?.Trim();

        if (!string.IsNullOrEmpty(ticketingSubdomain)
            && !string.Equals(
                ticketingSubdomain,
                deletion.Subdomain.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.ProfileName}' sends tickets to " +
                $"{ticketingSubdomain}.zendesk.com, but its Zendesk deletion " +
                $"connection '{deletion.ConnectionName}' points at " +
                $"{deletion.Subdomain}.zendesk.com. Bulk delete works only " +
                "on the instance the profile's tickets go to.");
        }

        return deletion;
    }
}
