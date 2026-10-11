using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    public async Task<ResolvedZendeskConnection>
        GetZendeskConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var environmentSubdomain =
            ReadEnvironmentValue(
                "ZENDESK_SUBDOMAIN"
            );

        var environmentClientId =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_ID"
            );

        var environmentClientSecret =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_SECRET"
            );

        if (environmentSubdomain is not null
            && environmentClientId is not null
            && environmentClientSecret is not null)
        {
            return new ResolvedZendeskConnection(
                environmentSubdomain,
                environmentClientId,
                environmentClientSecret
            );
        }

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
                $"SupportToolkit configure profile {active.Name}, or " +
                "provide all ZENDESK_* environment variables."
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

        var subdomain =
            environmentSubdomain
            ?? RequireConfiguredValue(
                connection.Subdomain,
                $"Zendesk connection '{connectionName}' subdomain"
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
            subdomain,
            clientId,
            clientSecret
        );
    }
}
