using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    public async Task<ResolvedAcronisConnection>
        GetAcronisConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var environmentDatacenterUrl =
            ReadEnvironmentValue(
                "ACRONIS_DATACENTER_URL"
            );

        var environmentClientId =
            ReadEnvironmentValue(
                "ACRONIS_CLIENT_ID"
            );

        var environmentClientSecret =
            ReadEnvironmentValue(
                "ACRONIS_CLIENT_SECRET"
            );

        /*
         * Preserve the original completely environment-driven workflow.
         *
         * If all three values are supplied explicitly, SupportToolkit does
         * not require a profile or persistent connection at all.
         */
        if (environmentDatacenterUrl is not null
            && environmentClientId is not null
            && environmentClientSecret is not null)
        {
            return new ResolvedAcronisConnection(
                environmentDatacenterUrl,
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
                active.Profile.AcronisConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "an Acronis connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}, or " +
                "provide all ACRONIS_* environment variables."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.AcronisConnection
                );

        var connection =
            FindAcronisConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references Acronis " +
                $"connection '{connectionName}', but that connection " +
                "does not exist."
            );
        }

        var datacenterUrl =
            environmentDatacenterUrl
            ?? RequireConfiguredValue(
                connection.DatacenterUrl,
                $"Acronis connection '{connectionName}' datacenter URL"
            );

        var clientId =
            environmentClientId
            ?? RequireConfiguredValue(
                connection.ClientId,
                $"Acronis connection '{connectionName}' client ID"
            );

        var clientSecret =
            environmentClientSecret
            ?? await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .AcronisClientSecret(
                        connectionName
                    ),
                cancellationToken
            );

        if (string.IsNullOrWhiteSpace(
                clientSecret))
        {
            throw new InvalidOperationException(
                $"Acronis client secret for connection " +
                $"'{connectionName}' is not configured. Run " +
                $"SupportToolkit configure profile {active.Name} " +
                "or provide ACRONIS_CLIENT_SECRET."
            );
        }

        return new ResolvedAcronisConnection(
            datacenterUrl,
            clientId,
            clientSecret
        );
    }
}
