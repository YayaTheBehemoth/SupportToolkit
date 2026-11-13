using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.ZendeskBulkDelete.Production;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;

namespace SupportToolkit.Composition;

/// <summary>
/// Builds Zendesk bulk delete for a host, from the profile's deletion
/// connection (never the ticketing connection). The connection is
/// resolved on every call, so a profile switched while the program runs
/// is used next time. Opening a session signs in to nothing.
/// </summary>
public static class ZendeskBulkDeleteComposition
{
    /// <summary>
    /// Local check only: whether the active profile has a usable deletion
    /// connection, and the instance it points at.
    /// </summary>
    public static async Task<BulkDeleteAvailability> GetAvailabilityAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        try
        {
            var connection =
                await configurationResolver
                    .GetZendeskDeletionConnectionAsync(cancellationToken);

            return new BulkDeleteAvailability(
                true,
                connection.Subdomain,
                null);
        }
        catch (InvalidOperationException exception)
        {
            return new BulkDeleteAvailability(
                false,
                null,
                exception.Message);
        }
    }

    /// <summary>
    /// The operator-facing session for the web UI, with deletion enabled:
    /// it deletes only after the operator's typed confirmations.
    /// </summary>
    public static async Task<BulkDeleteConnection> ConnectWithDeletionAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        CancellationToken cancellationToken)
    {
        var session =
            await OpenSessionAsync(
                configurationResolver,
                enableDeletion: true,
                cancellationToken: cancellationToken);

        return new BulkDeleteConnection(
            session.CreateReviewSession(),
            session);
    }

    /// <summary>
    /// The connection itself. Deletion is enabled only when asked; without
    /// it the session can search and preview, never delete.
    /// </summary>
    public static async Task<ZendeskBulkDeleteSession> OpenSessionAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        OperationalLogger? logger = null,
        bool enableDeletion = false,
        string? receiptDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        return ZendeskBulkDeleteSession.Create(
            await configurationResolver
                .GetZendeskDeletionConnectionAsync(cancellationToken),
            logger,
            enableDeletion,
            receiptDirectory);
    }
}
