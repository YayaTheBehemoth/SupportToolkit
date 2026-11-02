
namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    /// <summary>
    /// Resolves the active profile's Zendesk deletion connection.
    ///
    /// This is a different connection from the ticketing one resolved by
    /// GetZendeskConnectionAsync. It shares no credential, destination or
    /// user ID with it, never touches the secret store, and the ZENDESK_*
    /// environment overrides of the ticketing connection do not apply:
    /// the instance a delegated sign-in may act on comes only from the
    /// saved connection.
    /// </summary>
    public async Task<ResolvedZendeskDeletionConnection>
        GetZendeskDeletionConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        if (string.IsNullOrWhiteSpace(
                active.Profile.ZendeskDeletionConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "a Zendesk deletion connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.ZendeskDeletionConnection
                );

        var connection =
            configuration.Connections.ZendeskDeletion
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            connectionName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Value;

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references Zendesk " +
                $"deletion connection '{connectionName}', but that " +
                "connection does not exist."
            );
        }

        string Require(string value, string field) =>
            RequireConfiguredValue(
                value,
                $"Zendesk deletion connection '{connectionName}' " +
                field
            );

        return new ResolvedZendeskDeletionConnection(
            connectionName,
            Require(connection.Subdomain, "subdomain"),
            Require(connection.ClientId, "client ID")
        );
    }
}
