
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private async Task<int> ConfigureProfileAsync(
        string? requestedProfileName)
    {
        EnsureInteractiveConsole();

        var configuration =
            await _configurationStore.LoadAsync();

        var profileName =
            ResolveProfileName(
                configuration,
                requestedProfileName
            );

        var existingProfile =
            FindProfile(
                configuration,
                profileName
            );

        var profile =
            existingProfile
            ?? new SupportToolkitProfile();

        var pendingSecrets =
            new List<PendingSecret>();

        Console.WriteLine(
            "SupportToolkit profile configuration"
        );

        Console.WriteLine(
            "===================================="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Configuration file: {_configurationStore.ConfigurationPath}"
        );

        Console.WriteLine(
            $"Profile:            {profileName}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Runtime"
        );

        Console.WriteLine(
            "-------"
        );

        profile.Modules[
            BackupAggregatorCommand
        ] =
            ReadMode(
                "Backup Aggregator",
                GetConfiguredMode(
                    profile,
                    BackupAggregatorCommand
                )
            );

        profile.Modules[
            BackupHealthCommand
        ] =
            ReadMode(
                "Backup Health",
                GetConfiguredMode(
                    profile,
                    BackupHealthCommand
                )
            );

        Console.WriteLine();

        Console.WriteLine(
            "External writes"
        );

        Console.WriteLine(
            "---------------"
        );

        profile.AllowWrites =
            ReadConfirmation(
                profile.AllowWrites
                    ? "Allow external writes for this profile? [Y/n]: "
                    : "Allow external writes for this profile? [y/N]: ",
                profile.AllowWrites
            );

        if (RequiresAcronisConnection(
                profile))
        {
            Console.WriteLine();

            var pendingSecret =
                await ConfigureAcronisConnectionAsync(
                    configuration,
                    profile
                );

            if (pendingSecret is not null)
            {
                pendingSecrets.Add(
                    pendingSecret
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(
                profile.ZendeskConnection)
            || ReadConfirmation(
                "Configure Zendesk ticket submission for this profile? [y/N]: ",
                defaultValue: false
            ))
        {
            Console.WriteLine();

            var pendingSecret =
                await ConfigureZendeskConnectionAsync(
                    configuration,
                    profile
                );

            if (pendingSecret is not null)
            {
                pendingSecrets.Add(
                    pendingSecret
                );
            }
        }

        if (profile.AllowWrites
            && string.IsNullOrWhiteSpace(
                profile.ZendeskConnection))
        {
            Console.WriteLine();

            Console.WriteLine(
                "WARNING: external writes are enabled, but this profile " +
                "does not reference a Zendesk connection."
            );
        }

        UpsertProfile(
            configuration,
            profileName,
            profile
        );

        configuration.ActiveProfile =
            profileName;

        Console.WriteLine();

        if (!ReadConfirmation(
                "Save configuration? [Y/n]: ",
                defaultValue: true
            ))
        {
            Console.WriteLine(
                "Configuration was not changed."
            );

            return 0;
        }

        /*
         * Persist newly entered secrets before the JSON configuration.
         *
         * If configuration persistence subsequently fails, an unused
         * credential may remain in the secret store, but the configuration
         * file will never claim a missing credential was successfully stored.
         */
        foreach (var pendingSecret in pendingSecrets)
        {
            await _secretStore.SetSecretAsync(
                pendingSecret.Key,
                pendingSecret.Value
            );
        }

        await _configurationStore.SaveAsync(
            configuration
        );

        Console.WriteLine();

        Console.WriteLine(
            "Configuration saved."
        );

        Console.WriteLine(
            $"Active profile: {profileName}"
        );

        return 0;
    }

    private async Task<int> DeleteProfileAsync(
        string requestedProfileName)
    {
        EnsureInteractiveConsole();

        var configuration =
            await _configurationStore.LoadAsync();

        var profileName =
            SupportToolkitConfigurationNames
                .NormalizeProfileName(
                    requestedProfileName
                );

        var existingKey =
            FindProfileKey(
                configuration,
                profileName
            );

        if (existingKey is null)
        {
            Console.Error.WriteLine(
                $"ERROR: profile '{profileName}' does not exist."
            );

            return 1;
        }

        if (string.Equals(
                existingKey,
                configuration.ActiveProfile,
                StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                $"ERROR: profile '{profileName}' is currently active."
            );

            Console.Error.WriteLine(
                "Select another profile before deleting it."
            );

            return 1;
        }

        Console.WriteLine(
            $"Delete profile '{profileName}'?"
        );

        Console.WriteLine();

        Console.WriteLine(
            "The profile will be removed."
        );

        Console.WriteLine(
            "Referenced connections and stored credentials will not be deleted."
        );

        Console.WriteLine();

        if (!ReadConfirmation(
                "Continue? [y/N]: ",
                defaultValue: false
            ))
        {
            Console.WriteLine();

            Console.WriteLine(
                "Profile deletion cancelled."
            );

            return 0;
        }

        configuration.Profiles.Remove(
            existingKey
        );

        await _configurationStore.SaveAsync(
            configuration
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Profile '{profileName}' deleted."
        );

        return 0;
    }

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
                configuration.Connections
                    .Acronis.Keys
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
            && string.IsNullOrEmpty(
                secret))
        {
            Console.WriteLine(
                "WARNING: Acronis client secret is not configured."
            );
        }

        return string.IsNullOrEmpty(
                secret)
            ? null
            : new PendingSecret(
                secretKey,
                secret
            );
    }

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

        /*
         * Selecting a connection and creating a new connection are
         * deliberately different operations.
         *
         * A destination name must never accidentally become a new
         * connection name.
         */
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

            /*
             * If this subdomain is already registered, the operator
             * most likely wants another ticket destination, not another
             * credential set.
             */
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
        }

        /*
         * Moving a profile to another connection requires the operator
         * to select its destination again.
         */
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

        /*
         * Reusing an existing connection must not force the user to
         * supply credentials again.
         */
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

    private async Task<int> UseProfileAsync(
        string requestedProfileName)
    {
        var configuration =
            await _configurationStore.LoadAsync();

        var profileName =
            SupportToolkitConfigurationNames
                .NormalizeProfileName(
                    requestedProfileName
                );

        var profile =
            FindProfile(
                configuration,
                profileName
            );

        if (profile is null)
        {
            Console.Error.WriteLine(
                $"ERROR: profile '{profileName}' does not exist."
            );

            return 1;
        }

        configuration.ActiveProfile =
            profileName;

        await _configurationStore.SaveAsync(
            configuration
        );

        Console.WriteLine(
            $"Active profile: {profileName}"
        );

        return 0;
    }
}
