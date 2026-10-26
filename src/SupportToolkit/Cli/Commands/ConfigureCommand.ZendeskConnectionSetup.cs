
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private async Task<PendingSecret?>
        ConfigureZendeskConnectionAsync(
            SupportToolkitConfiguration configuration,
            SupportToolkitProfile profile)
    {
        Console.WriteLine(
            "Zendesk connection"
        );

        Console.WriteLine(
            "------------------"
        );

        var selection =
            SelectZendeskConnection(
                configuration,
                profile
            );

        var connectionName =
            selection.ConnectionName;

        var isNewConnection =
            selection.IsNew;

        var connection =
            FindZendeskConnection(
                configuration,
                connectionName
            )
            ?? new ZendeskConnectionConfiguration();

        var previousSubdomain =
            connection.Subdomain;

        if (isNewConnection)
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Creating Zendesk connection '{connectionName}'."
            );

            ConfigureZendeskMetadata(
                connection
            );

            var matchingConnection =
                configuration.Connections.Zendesk
                    .FirstOrDefault(
                        pair =>
                            string.Equals(
                                pair.Value.Subdomain,
                                connection.Subdomain,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

            if (!string.IsNullOrWhiteSpace(
                matchingConnection.Key))
            {
                Console.WriteLine();

                Console.WriteLine(
                    "WARNING: This Zendesk instance is already configured."
                );

                Console.WriteLine(
                    $"Existing connection: {matchingConnection.Key}"
                );

                Console.WriteLine(
                    "You can reuse that connection and select a different " +
                    "ticket destination without entering credentials again."
                );

                if (!ReadConfirmation(
                        "Create a separate connection anyway? [y/N]: ",
                        defaultValue: false
                    ))
                {
                    connectionName =
                        matchingConnection.Key;

                    connection =
                        matchingConnection.Value;

                    previousSubdomain =
                        connection.Subdomain;

                    isNewConnection =
                        false;

                    Console.WriteLine();

                    Console.WriteLine(
                        $"Reusing existing connection '{connectionName}'."
                    );
                }
            }
        }
        else
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Using existing connection '{connectionName}'."
            );

            Console.WriteLine(
                $"Subdomain: {DisplayValue(connection.Subdomain)}"
            );

            Console.WriteLine(
                $"Client ID: {DisplayValue(connection.ClientId)}"
            );

            Console.WriteLine(
                $"Submitter user ID: {DisplayZendeskUserId(connection.SubmitterId)}"
            );

            Console.WriteLine(
                $"Requester user ID: {DisplayZendeskUserId(connection.RequesterId)}"
            );

            Console.WriteLine();

            if (ReadConfirmation(
                    "Update connection metadata? [y/N]: ",
                    defaultValue: false
                ))
            {
                ConfigureZendeskMetadata(
                    connection
                );
            }
            else if (!HasValidZendeskTicketIdentity(
                connection))
            {
                /*
                 * Connections saved before these settings existed
                 * cannot submit tickets until both IDs are set.
                 */
                Console.WriteLine(
                    "This connection has no valid submitter and " +
                    "requester user IDs, which ticket submission requires."
                );

                ConfigureZendeskTicketIdentity(
                    connection
                );
            }
        }

        if (!string.Equals(
                profile.ZendeskConnection,
                connectionName,
                StringComparison.OrdinalIgnoreCase))
        {
            profile.ZendeskDestination = null;
        }

        profile.ZendeskConnection =
            connectionName;

        InvalidateZendeskDestinationsIfInstanceChanged(
            connection,
            profile,
            previousSubdomain
        );

        ConfigureZendeskDestinations(
            connection,
            profile
        );

        UpsertZendeskConnection(
            configuration,
            connectionName,
            connection
        );

        var secretKey =
            SupportToolkitSecretKeys
                .ZendeskClientSecret(
                    connectionName
                );

        var secretConfigured =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    secretKey
                )
            );

        Console.WriteLine();

        Console.WriteLine(
            $"Client secret: {DisplaySecretStatus(secretConfigured)}"
        );

        var updateSecret =
            isNewConnection
            || !secretConfigured
            || ReadConfirmation(
                "Update stored client secret? [y/N]: ",
                defaultValue: false
            );

        var secret =
            updateSecret
                ? ReadSecret(
                    "Client secret",
                    secretConfigured
                )
                : string.Empty;

        if (!secretConfigured
            && string.IsNullOrEmpty(secret))
        {
            Console.WriteLine(
                "WARNING: Zendesk client secret is not configured."
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
