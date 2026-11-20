using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    /// <summary>
    /// The active profile's ticketing selection, read the same way as
    /// every resolution reads it. Null when the profile sends tickets
    /// nowhere.
    /// </summary>
    public async Task<TicketingSelection?> GetTicketingSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(cancellationToken);

        return TicketingSelection.For(
            GetRequiredActiveProfile(configuration).Profile);
    }

    /// <summary>
    /// The active profile's osTicket connection and destination,
    /// validated locally. Nothing is called and the key is not checked
    /// against osTicket. Like every destination, it requires an active
    /// profile that selects it explicitly.
    /// </summary>
    public async Task<ResolvedOsTicketConnection>
        GetOsTicketConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(cancellationToken);

        var active = GetRequiredActiveProfile(configuration);

        var selection = TicketingSelection.For(active.Profile);

        if (selection is null
            || !selection.Is(TicketingSelection.Providers.OsTicket))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not send tickets " +
                "to osTicket.");
        }

        if (string.IsNullOrWhiteSpace(selection.Connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' selects osTicket but " +
                "names no connection (ticketing.connection).");
        }

        var connectionName =
            SupportToolkitConfigurationNames.NormalizeConnectionName(
                selection.Connection);

        var connection =
            configuration.Connections.OsTicket
                .FirstOrDefault(pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase))
                .Value;

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references osTicket " +
                $"connection '{connectionName}', but that connection " +
                "does not exist.");
        }

        if (string.IsNullOrWhiteSpace(selection.Destination))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not select " +
                "an osTicket destination (ticketing.destination). " +
                "Ticket submission requires an explicitly configured " +
                "destination.");
        }

        var destinationName =
            SupportToolkitConfigurationNames.NormalizeConnectionName(
                selection.Destination);

        var destination =
            connection.Destinations
                .FirstOrDefault(pair =>
                    string.Equals(
                        pair.Key,
                        destinationName,
                        StringComparison.OrdinalIgnoreCase))
                .Value;

        if (destination is null)
        {
            throw new InvalidOperationException(
                $"osTicket destination '{destinationName}' does not " +
                $"exist on connection '{connectionName}'.");
        }

        if (destination.TopicId <= 0)
        {
            throw new InvalidOperationException(
                $"osTicket destination '{destinationName}' has an " +
                "invalid help topic ID. A positive topicId is required.");
        }

        var baseUrl =
            RequireConfiguredValue(
                connection.BaseUrl,
                $"osTicket connection '{connectionName}' baseUrl");

        var requesterName =
            RequireConfiguredValue(
                connection.RequesterName,
                $"osTicket connection '{connectionName}' requesterName");

        var requesterEmail =
            RequireConfiguredValue(
                connection.RequesterEmail,
                $"osTicket connection '{connectionName}' requesterEmail");

        var key = SupportToolkitSecretKeys.OsTicketApiKey(connectionName);

        var apiKey =
            await ReadConnectionSecretAsync(
                configuration,
                key,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "osTicket API key for connection " +
                $"'{connectionName}' is not configured. " +
                MissingSecretAdvice(key, active.Name));
        }

        return new ResolvedOsTicketConnection(
            connectionName,
            baseUrl,
            apiKey,
            requesterName,
            requesterEmail,
            destinationName,
            destination.TopicId);
    }
}
