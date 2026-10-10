using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Interactive operator command for configuring SupportToolkit.
///
/// Profiles define runtime behavior.
/// Connections define reusable external provider endpoints.
/// Authentication secrets are stored separately through ISecretStore.
///
/// This is a host-level command rather than an operational module.
/// </summary>
public sealed class ConfigureCommand
{
    private const string BackupAggregatorCommand =
        "backup-aggregator";

    private const string BackupHealthCommand =
        "backup-health";

    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly ISecretStore
        _secretStore;

    public ConfigureCommand(
        ISupportToolkitConfigurationStore configurationStore,
        ISecretStore secretStore)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );

        _secretStore =
            secretStore
            ?? throw new ArgumentNullException(
                nameof(secretStore)
            );
    }

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length == 0)
        {
            return await ConfigureProfileAsync(
                requestedProfileName:
                    null
            );
        }

        if (args.Length == 1
            && string.Equals(
                args[0],
                "status",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await WriteStatusAsync();
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "profile",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await ConfigureProfileAsync(
                args[1]
            );
        }

        if (args.Length == 3
            && string.Equals(
                args[0],
                "profile",
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                args[1],
                "delete",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await DeleteProfileAsync(
                args[2]
            );
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "use",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await UseProfileAsync(
                args[1]
            );
        }

        if (args.Length == 1
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        PrintUsage();

        return 1;
    }

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
                defaultValue:
                    false
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
                defaultValue:
                    true
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
                defaultValue:
                    false
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
                    defaultValue:
                        false
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

        var defaultConnectionName =
            GetDefaultConnectionName(
                profile.ZendeskConnection,
                configuration.Connections
                    .Zendesk.Keys
            );

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    ReadRequiredWithDefault(
                        "Connection name",
                        defaultConnectionName
                    )
                );

        profile.ZendeskConnection =
            connectionName;

        var existingConnection =
            FindZendeskConnection(
                configuration,
                connectionName
            );

        var connection =
            existingConnection
            ?? new ZendeskConnectionConfiguration();

        if (existingConnection is not null)
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
                    defaultValue:
                        false
                ))
            {
                ConfigureZendeskMetadata(
                    connection
                );
            }
        }
        else
        {
            Console.WriteLine();

            Console.WriteLine(
                $"Creating Zendesk connection '{connectionName}'."
            );

            ConfigureZendeskMetadata(
                connection
            );
        }

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
                "WARNING: Zendesk client secret is not configured."
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

    private async Task<int> WriteStatusAsync()
    {
        var configuration =
            await _configurationStore.LoadAsync();

        Console.WriteLine(
            "SupportToolkit configuration"
        );

        Console.WriteLine(
            "============================"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Configuration file: {_configurationStore.ConfigurationPath}"
        );

        Console.WriteLine(
            $"Active profile:     {configuration.ActiveProfile}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Profiles"
        );

        Console.WriteLine(
            "--------"
        );

        if (configuration.Profiles.Count == 0)
        {
            Console.WriteLine(
                "No profiles configured."
            );
        }
        else
        {
            foreach (var pair in configuration.Profiles
                         .OrderBy(
                             pair =>
                                 pair.Key,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                var marker =
                    string.Equals(
                        pair.Key,
                        configuration.ActiveProfile,
                        StringComparison.OrdinalIgnoreCase
                    )
                        ? "*"
                        : " ";

                var profile =
                    pair.Value;

                Console.WriteLine(
                    $"{marker} {pair.Key}"
                );

                Console.WriteLine(
                    $"    External writes:    " +
                    $"{FormatBoolean(profile.AllowWrites)}"
                );

                Console.WriteLine(
                    $"    Acronis connection: " +
                    $"{DisplayOptionalValue(profile.AcronisConnection)}"
                );

                Console.WriteLine(
                    $"    Zendesk connection: " +
                    $"{DisplayOptionalValue(profile.ZendeskConnection)}"
                );

                Console.WriteLine(
                    $"    Backup Aggregator:  " +
                    $"{FormatMode(GetConfiguredMode(profile, BackupAggregatorCommand))}"
                );

                Console.WriteLine(
                    $"    Backup Health:      " +
                    $"{FormatMode(GetConfiguredMode(profile, BackupHealthCommand))}"
                );
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "Acronis connections"
        );

        Console.WriteLine(
            "-------------------"
        );

        if (configuration.Connections.Acronis.Count == 0)
        {
            Console.WriteLine(
                "No Acronis connections configured."
            );
        }
        else
        {
            foreach (var pair in configuration.Connections
                         .Acronis
                         .OrderBy(
                             pair =>
                                 pair.Key,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                var secretConfigured =
                    !string.IsNullOrWhiteSpace(
                        await _secretStore.GetSecretAsync(
                            SupportToolkitSecretKeys
                                .AcronisClientSecret(
                                    pair.Key
                                )
                        )
                    );

                Console.WriteLine(
                    pair.Key
                );

                Console.WriteLine(
                    $"    Datacenter URL: {DisplayValue(pair.Value.DatacenterUrl)}"
                );

                Console.WriteLine(
                    $"    Client ID:      {DisplayValue(pair.Value.ClientId)}"
                );

                Console.WriteLine(
                    $"    Client secret:  {DisplaySecretStatus(secretConfigured)}"
                );
            }
        }

        Console.WriteLine();

        Console.WriteLine(
            "Zendesk connections"
        );

        Console.WriteLine(
            "-------------------"
        );

        if (configuration.Connections.Zendesk.Count == 0)
        {
            Console.WriteLine(
                "No Zendesk connections configured."
            );
        }
        else
        {
            foreach (var pair in configuration.Connections
                         .Zendesk
                         .OrderBy(
                             pair =>
                                 pair.Key,
                             StringComparer.OrdinalIgnoreCase
                         ))
            {
                var secretConfigured =
                    !string.IsNullOrWhiteSpace(
                        await _secretStore.GetSecretAsync(
                            SupportToolkitSecretKeys
                                .ZendeskClientSecret(
                                    pair.Key
                                )
                        )
                    );

                Console.WriteLine(
                    pair.Key
                );

                Console.WriteLine(
                    $"    Subdomain:     {DisplayValue(pair.Value.Subdomain)}"
                );

                Console.WriteLine(
                    $"    Client ID:     {DisplayValue(pair.Value.ClientId)}"
                );

                Console.WriteLine(
                    $"    Client secret: {DisplaySecretStatus(secretConfigured)}"
                );
            }
        }

        return 0;
    }

    private static string ResolveProfileName(
        SupportToolkitConfiguration configuration,
        string? requestedProfileName)
    {
        if (!string.IsNullOrWhiteSpace(
                requestedProfileName))
        {
            return SupportToolkitConfigurationNames
                .NormalizeProfileName(
                    requestedProfileName
                );
        }

        var defaultProfileName =
            string.IsNullOrWhiteSpace(
                configuration.ActiveProfile)
                ? "local-dev"
                : configuration.ActiveProfile;

        return SupportToolkitConfigurationNames
            .NormalizeProfileName(
                ReadWithDefault(
                    "Profile name",
                    defaultProfileName
                )
            );
    }

    private static bool RequiresAcronisConnection(
        SupportToolkitProfile profile)
    {
        return GetConfiguredMode(
                   profile,
                   BackupAggregatorCommand
               )
               == SupportToolkitMode.Production
            || GetConfiguredMode(
                   profile,
                   BackupHealthCommand
               )
               == SupportToolkitMode.Production;
    }

    private static string? FindProfileKey(
        SupportToolkitConfiguration configuration,
        string profileName)
    {
        return configuration.Profiles.Keys
            .FirstOrDefault(
                key =>
                    string.Equals(
                        key,
                        profileName,
                        StringComparison.OrdinalIgnoreCase
                    )
            );
    }

    private static void ConfigureAcronisMetadata(
        AcronisConnectionConfiguration connection)
    {
        connection.DatacenterUrl =
            ReadRequiredWithDefault(
                "Datacenter URL",
                connection.DatacenterUrl
            );

        connection.ClientId =
            ReadRequiredWithDefault(
                "Client ID",
                connection.ClientId
            );
    }

    private static void ConfigureZendeskMetadata(
        ZendeskConnectionConfiguration connection)
    {
        connection.Subdomain =
            ReadRequiredWithDefault(
                "Subdomain",
                connection.Subdomain
            );

        connection.ClientId =
            ReadRequiredWithDefault(
                "Client ID",
                connection.ClientId
            );
    }

    private static string GetDefaultConnectionName(
        string? profileConnection,
        IEnumerable<string> configuredConnections)
    {
        if (!string.IsNullOrWhiteSpace(
                profileConnection))
        {
            return profileConnection;
        }

        var connectionNames =
            configuredConnections
                .ToList();

        return connectionNames.Count == 1
            ? connectionNames[0]
            : string.Empty;
    }

    private static SupportToolkitProfile? FindProfile(
        SupportToolkitConfiguration configuration,
        string profileName)
    {
        return configuration.Profiles
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        profileName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    private static AcronisConnectionConfiguration?
        FindAcronisConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.Acronis
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    private static ZendeskConnectionConfiguration?
        FindZendeskConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.Zendesk
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    private static void UpsertProfile(
        SupportToolkitConfiguration configuration,
        string profileName,
        SupportToolkitProfile profile)
    {
        RemoveKeyIgnoringCase(
            configuration.Profiles,
            profileName
        );

        configuration.Profiles[
            profileName
        ] =
            profile;
    }

    private static void UpsertAcronisConnection(
        SupportToolkitConfiguration configuration,
        string connectionName,
        AcronisConnectionConfiguration connection)
    {
        RemoveKeyIgnoringCase(
            configuration.Connections.Acronis,
            connectionName
        );

        configuration.Connections.Acronis[
            connectionName
        ] =
            connection;
    }

    private static void UpsertZendeskConnection(
        SupportToolkitConfiguration configuration,
        string connectionName,
        ZendeskConnectionConfiguration connection)
    {
        RemoveKeyIgnoringCase(
            configuration.Connections.Zendesk,
            connectionName
        );

        configuration.Connections.Zendesk[
            connectionName
        ] =
            connection;
    }

    private static void RemoveKeyIgnoringCase<TValue>(
        IDictionary<string, TValue> dictionary,
        string key)
    {
        var existingKey =
            dictionary.Keys
                .FirstOrDefault(
                    candidate =>
                        string.Equals(
                            candidate,
                        key,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        if (existingKey is not null)
        {
            dictionary.Remove(
                existingKey
            );
        }
    }

    private static SupportToolkitMode GetConfiguredMode(
        SupportToolkitProfile profile,
        string moduleCommand)
    {
        var match =
            profile.Modules.FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        moduleCommand,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

        return string.IsNullOrEmpty(
                match.Key)
            ? SupportToolkitMode.Fixture
            : match.Value;
    }

    private static string ReadWithDefault(
        string label,
        string defaultValue)
    {
        Console.Write(
            $"{label} [{defaultValue}]: "
        );

        var value =
            Console.ReadLine();

        return string.IsNullOrWhiteSpace(
                value)
            ? defaultValue
            : value.Trim();
    }

    private static string ReadRequiredWithDefault(
        string label,
        string currentValue)
    {
        while (true)
        {
            var hasCurrentValue =
                !string.IsNullOrWhiteSpace(
                    currentValue
                );

            Console.Write(
                hasCurrentValue
                    ? $"{label} [{currentValue}]: "
                    : $"{label}: "
            );

            var value =
                Console.ReadLine();

            if (!string.IsNullOrWhiteSpace(
                    value))
            {
                return value.Trim();
            }

            if (hasCurrentValue)
            {
                return currentValue.Trim();
            }

            Console.WriteLine(
                $"{label} is required."
            );
        }
    }

    private static string ReadSecret(
        string label,
        bool secretAlreadyExists)
    {
        var prompt =
            secretAlreadyExists
                ? $"{label} [configured - Enter keeps current]: "
                : $"{label} [Enter to leave unconfigured]: ";

        return ConsoleSecretReader.ReadOptional(
            prompt
        );
    }

    private static SupportToolkitMode ReadMode(
        string label,
        SupportToolkitMode defaultMode)
    {
        while (true)
        {
            Console.Write(
                $"{label} mode " +
                $"[{FormatMode(defaultMode)}]: "
            );

            var value =
                Console.ReadLine();

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return defaultMode;
            }

            if (value.Equals(
                    "fixture",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SupportToolkitMode.Fixture;
            }

            if (value.Equals(
                    "production",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SupportToolkitMode.Production;
            }

            Console.WriteLine(
                "Expected 'fixture' or 'production'."
            );
        }
    }

    private static bool ReadConfirmation(
        string prompt,
        bool defaultValue)
    {
        while (true)
        {
            Console.Write(
                prompt
            );

            var value =
                Console.ReadLine();

            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return defaultValue;
            }

            if (value.Equals(
                    "y",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "yes",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value.Equals(
                    "n",
                    StringComparison.OrdinalIgnoreCase)
                || value.Equals(
                    "no",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Console.WriteLine(
                "Expected 'y' or 'n'."
            );
        }
    }

    private static string DisplayValue(
        string value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "missing"
            : value;
    }

    private static string DisplayOptionalValue(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "none"
            : value;
    }

    private static string DisplaySecretStatus(
        bool configured)
    {
        return configured
            ? "configured"
            : "missing";
    }

    private static string FormatBoolean(
        bool value)
    {
        return value
            ? "enabled"
            : "disabled";
    }

    private static string FormatMode(
        SupportToolkitMode mode)
    {
        return mode
            .ToString()
            .ToLowerInvariant();
    }

    private static void EnsureInteractiveConsole()
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                "Interactive configuration requires an interactive console."
            );
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            "Configuration"
        );

        Console.WriteLine();

        Console.WriteLine(
            "  SupportToolkit configure"
        );

        Console.WriteLine(
            "  SupportToolkit configure profile <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure profile delete <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure use <name>"
        );

        Console.WriteLine(
            "  SupportToolkit configure status"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Profiles define runtime behavior, external write permission, " +
            "and reusable provider connections."
        );

        Console.WriteLine(
            "Deleting a profile does not delete shared provider " +
            "connections or stored credentials."
        );

        Console.WriteLine(
            "Authentication secrets are stored separately in the " +
            "configured secret store."
        );
    }

    private sealed record PendingSecret(
        string Key,
        string Value
    );
}