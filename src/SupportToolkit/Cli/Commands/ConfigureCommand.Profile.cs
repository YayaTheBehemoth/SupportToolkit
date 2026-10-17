
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

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

        Console.WriteLine("Runtime");
        Console.WriteLine("-------");

        profile.Modules[BackupAggregatorCommand] =
            ReadMode(
                "Backup Aggregator",
                GetConfiguredMode(
                    profile,
                    BackupAggregatorCommand
                )
            );

        profile.Modules[BackupHealthCommand] =
            ReadMode(
                "Backup Health",
                GetConfiguredMode(
                    profile,
                    BackupHealthCommand
                )
            );

        if (RequiresAcronisConnection(profile))
        {
            Console.WriteLine();

            var keepExisting =
                await KeepExistingAcronisConnectionAsync(
                    configuration,
                    profile
                );

            if (!keepExisting)
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
        }

        var zendeskSelected =
            !string.IsNullOrWhiteSpace(
                profile.ZendeskConnection
            );

        if (zendeskSelected
            || ReadConfirmation(
                "Configure Zendesk ticket submission for this profile? [y/N]: ",
                defaultValue: false
            ))
        {
            Console.WriteLine();

            var keepExisting =
                zendeskSelected
                && await KeepExistingZendeskConnectionAsync(
                    configuration,
                    profile
                );

            if (!keepExisting)
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
        }

        /*
         * Profile configuration and profile activation are distinct.
         *
         * The first profile must become active during onboarding.
         * Otherwise, changing a different profile does not change
         * the active profile unless the operator explicitly requests it.
         */
        var isFirstProfile =
            configuration.Profiles.Count == 0;

        var isAlreadyActive =
            string.Equals(
                configuration.ActiveProfile,
                profileName,
                StringComparison.OrdinalIgnoreCase
            )
            && existingProfile is not null;

        var activateProfile =
            isFirstProfile;

        Console.WriteLine();

        if (isFirstProfile)
        {
            Console.WriteLine(
                "This is the first profile and will be activated " +
                "when configuration is saved."
            );
        }
        else if (isAlreadyActive)
        {
            Console.WriteLine(
                $"Profile '{profileName}' is already active."
            );
        }
        else
        {
            Console.WriteLine(
                $"Current active profile: {configuration.ActiveProfile}"
            );

            Console.WriteLine();

            activateProfile =
                ReadConfirmation(
                    $"Activate profile '{profileName}' after saving? [y/N]: ",
                    defaultValue: false
                );
        }

        /*
         * Record the profile before saving, but do not change
         * activation unless requested or required for first-run.
         */
        UpsertProfile(
            configuration,
            profileName,
            profile
        );

        if (activateProfile)
        {
            configuration.ActiveProfile =
                profileName;
        }

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
            $"Profile '{profileName}' saved."
        );

        if (activateProfile)
        {
            Console.WriteLine(
                $"Active profile: {profileName}"
            );
        }
        else
        {
            Console.WriteLine(
                $"Active profile unchanged: {configuration.ActiveProfile}"
            );

            Console.WriteLine(
                $"To activate '{profileName}' later, run: " +
                $"SupportToolkit configure use {profileName}"
            );
        }

        return 0;
    }

    private async Task<bool> KeepExistingAcronisConnectionAsync(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.AcronisConnection))
        {
            return false;
        }

        var connectionName =
            profile.AcronisConnection;

        var connection =
            FindAcronisConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            Console.WriteLine(
                $"Acronis connection '{connectionName}' no longer exists."
            );

            Console.WriteLine(
                "Connection setup is required."
            );

            return false;
        }

        var secret =
            await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .AcronisClientSecret(
                        connectionName
                    )
            );

        AcronisOptions options;

        try
        {
            options =
                AcronisOptions.Create(
                    connection.DatacenterUrl,
                    connection.ClientId,
                    secret ?? string.Empty
                );
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine(
                $"Acronis connection '{connectionName}' is incomplete " +
                "or invalid."
            );

            Console.WriteLine(
                "Connection setup is required."
            );

            return false;
        }

        Console.WriteLine(
            "Existing Acronis configuration"
        );

        Console.WriteLine(
            "------------------------------"
        );

        Console.WriteLine(
            $"Connection: {connectionName}"
        );

        Console.WriteLine(
            $"Datacenter: {options.DatacenterUrl}"
        );

        Console.WriteLine(
            "Client secret: configured"
        );

        Console.WriteLine();

        var keep =
            ReadConfirmation(
                "Keep existing Acronis configuration? [Y/n]: ",
                defaultValue: true
            );

        if (keep)
        {
            Console.WriteLine(
                "Acronis configuration kept unchanged."
            );
        }

        return keep;
    }

    private async Task<bool> KeepExistingZendeskConnectionAsync(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.ZendeskConnection))
        {
            return false;
        }

        var connectionName =
            profile.ZendeskConnection;

        var connection =
            FindZendeskConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            Console.WriteLine(
                $"Zendesk connection '{connectionName}' no longer exists."
            );

            Console.WriteLine(
                "Connection setup is required."
            );

            return false;
        }

        var destinationName =
            profile.ZendeskDestination;

        var destination =
            string.IsNullOrWhiteSpace(destinationName)
                ? null
                : connection.Destinations
                    .FirstOrDefault(
                        pair =>
                            string.Equals(
                                pair.Key,
                                destinationName,
                                StringComparison.OrdinalIgnoreCase
                            )
                    )
                    .Value;

        if (destination is null
            || destination.GroupId <= 0)
        {
            Console.WriteLine(
                "The existing Zendesk destination is missing " +
                "or invalid."
            );

            Console.WriteLine(
                "Zendesk destination setup is required."
            );

            return false;
        }

        var secret =
            await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .ZendeskClientSecret(
                        connectionName
                    )
            );

        ZendeskOptions options;

        try
        {
            options =
                ZendeskOptions.Create(
                    connection.Subdomain,
                    connection.ClientId,
                    secret ?? string.Empty
                );
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine(
                $"Zendesk connection '{connectionName}' is incomplete " +
                "or invalid."
            );

            Console.WriteLine(
                "Connection setup is required."
            );

            return false;
        }

        Console.WriteLine(
            "Existing Zendesk configuration"
        );

        Console.WriteLine(
            "------------------------------"
        );

        Console.WriteLine(
            $"Connection:  {connectionName}"
        );

        Console.WriteLine(
            $"Instance:    {options.BaseUri.GetLeftPart(UriPartial.Authority)}"
        );

        Console.WriteLine(
            $"Destination: {destinationName}"
        );

        Console.WriteLine(
            $"Group ID:    {destination.GroupId}"
        );

        Console.WriteLine(
            "Client secret: configured"
        );

        Console.WriteLine();

        var keep =
            ReadConfirmation(
                "Keep existing Zendesk configuration? [Y/n]: ",
                defaultValue: true
            );

        if (keep)
        {
            Console.WriteLine(
                "Zendesk configuration kept unchanged."
            );
        }

        return keep;
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
