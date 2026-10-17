
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private async Task<PendingSecret?>
        ConfigureAcronisConnectionAsync(
            SupportToolkitConfiguration configuration,
            SupportToolkitProfile profile)
    {
        Console.WriteLine(
            "Acronis connection"
        );

        Console.WriteLine(
            "------------------"
        );

        var defaultConnectionName =
            GetDefaultConnectionName(
                profile.AcronisConnection,
                configuration.Connections.Acronis.Keys
            );

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    ReadRequiredWithDefault(
                        "Connection name",
                        defaultConnectionName
                    )
                );

        profile.AcronisConnection =
            connectionName;

        var existingConnection =
            FindAcronisConnection(
                configuration,
                connectionName
            );

        var connection =
            existingConnection
            ?? new AcronisConnectionConfiguration();

        if (existingConnection is not null)
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Using existing connection '{connectionName}'."
            );

            Console.WriteLine(
                $"Datacenter URL: {DisplayValue(connection.DatacenterUrl)}"
            );

            Console.WriteLine(
                $"Client ID:      {DisplayValue(connection.ClientId)}"
            );

            Console.WriteLine();

            if (ReadConfirmation(
                "Update connection metadata? [y/N]: ",
                defaultValue: false
            ))
            {
                ConfigureAcronisMetadata(
                    connection
                );
            }
        }
        else
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Creating Acronis connection '{connectionName}'."
            );

            ConfigureAcronisMetadata(
                connection
            );
        }

        UpsertAcronisConnection(
            configuration,
            connectionName,
            connection
        );

        var secretKey =
            SupportToolkitSecretKeys
                .AcronisClientSecret(
                    connectionName
                );

        var secretConfigured =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    secretKey
                )
            );

        var secret =
            ReadSecret(
                "Client secret",
                secretConfigured
            );

        if (!secretConfigured
            && string.IsNullOrEmpty(secret))
        {
            Console.WriteLine(
                "WARNING: Acronis client secret is not configured."
            );
        }

        return string.IsNullOrEmpty(secret)
            ? null
            : new PendingSecret(
                secretKey,
                secret
            );
    }
}
