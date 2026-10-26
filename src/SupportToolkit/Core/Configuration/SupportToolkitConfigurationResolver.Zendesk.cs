
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    public async Task<ResolvedZendeskConnection>
        GetZendeskConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Ticket destinations always require an active profile.
         *
         * Environment credentials alone cannot bypass destination
         * selection or silently change the Zendesk instance.
         */
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        if (string.IsNullOrWhiteSpace(
            active.Profile.ZendeskConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "a Zendesk connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.ZendeskConnection
                );

        var connection =
            FindZendeskConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references Zendesk " +
                $"connection '{connectionName}', but that connection " +
                "does not exist."
            );
        }

        if (string.IsNullOrWhiteSpace(
            active.Profile.ZendeskDestination))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not select " +
                "a Zendesk destination. Ticket submission requires " +
                "an explicitly configured destination."
            );
        }

        var destinationName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.ZendeskDestination
                );

        var destination =
            connection.Destinations
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            destinationName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Value;

        if (destination is null)
        {
            throw new InvalidOperationException(
                $"Zendesk destination '{destinationName}' does not " +
                $"exist on connection '{connectionName}'."
            );
        }

        if (destination.GroupId <= 0)
        {
            throw new InvalidOperationException(
                $"Zendesk destination '{destinationName}' has an " +
                "invalid group ID. A positive group ID is required."
            );
        }

        /*
         * Without explicit IDs Zendesk attributes tickets to the user
         * the token acts as. Fail closed rather than fall back to it.
         */
        var submitterId =
            RequireZendeskUserId(
                connection.SubmitterId,
                connectionName,
                "submitter",
                "submitterId",
                active.Name
            );

        var requesterId =
            RequireZendeskUserId(
                connection.RequesterId,
                connectionName,
                "requester",
                "requesterId",
                active.Name
            );

        var configuredSubdomain =
            RequireConfiguredValue(
                connection.Subdomain,
                $"Zendesk connection '{connectionName}' subdomain"
            );

        var environmentSubdomain =
            ReadEnvironmentValue(
                "ZENDESK_SUBDOMAIN"
            );

        /*
         * A group ID belongs to a particular Zendesk instance.
         *
         * Allow credential overrides for that instance, but reject
         * an environment override that redirects to another instance.
         */
        if (environmentSubdomain is not null
            && !string.Equals(
                environmentSubdomain,
                configuredSubdomain,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ZENDESK_SUBDOMAIN does not match the configured " +
                $"Zendesk connection '{connectionName}'. " +
                "Ticket routing cannot switch instances through " +
                "an environment override."
            );
        }

        var environmentClientId =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_ID"
            );

        var environmentClientSecret =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_SECRET"
            );

        var clientId =
            environmentClientId
            ?? RequireConfiguredValue(
                connection.ClientId,
                $"Zendesk connection '{connectionName}' client ID"
            );

        var clientSecret =
            environmentClientSecret
            ?? await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .ZendeskClientSecret(
                        connectionName
                    ),
                cancellationToken
            );

        if (string.IsNullOrWhiteSpace(
            clientSecret))
        {
            throw new InvalidOperationException(
                $"Zendesk client secret for connection " +
                $"'{connectionName}' is not configured. Run " +
                $"SupportToolkit configure profile {active.Name} " +
                "or provide ZENDESK_CLIENT_SECRET."
            );
        }

        return new ResolvedZendeskConnection(
            configuredSubdomain,
            clientId,
            clientSecret,
            destinationName,
            destination.GroupId,
            submitterId,
            requesterId
        );
    }

    private static long RequireZendeskUserId(
        long? value,
        string connectionName,
        string role,
        string settingName,
        string profileName)
    {
        if (value is null)
        {
            throw new InvalidOperationException(
                $"Zendesk connection '{connectionName}' has no " +
                $"{role} user ID. Ticket submission requires an " +
                "explicit Zendesk user ID. Run SupportToolkit " +
                $"configure profile {profileName} or set " +
                $"connections.zendesk.{connectionName}.{settingName} " +
                "in the configuration file."
            );
        }

        if (value <= 0)
        {
            throw new InvalidOperationException(
                $"Zendesk connection '{connectionName}' has an " +
                $"invalid {role} user ID. A positive Zendesk user " +
                "ID is required."
            );
        }

        return value.Value;
    }
}
