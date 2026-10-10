using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Interactive operator command for configuring SupportToolkit.
///
/// Non-secret settings are persisted through the configuration store.
/// Authentication secrets are persisted separately through ISecretStore.
///
/// This is a host-level command rather than an operational SupportToolkit
/// module.
/// </summary>
public sealed class ConfigureCommand
{
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
            return await ConfigureAsync();
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

        if (args.Length == 1
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        PrintUsage();

        return 1;
    }

    private async Task<int> ConfigureAsync()
    {
        EnsureInteractiveConsole();

        var configuration =
            await _configurationStore.LoadAsync();

        var defaultProfileName =
            string.IsNullOrWhiteSpace(
                configuration.ActiveProfile)
                ? "development"
                : configuration.ActiveProfile;

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

        Console.WriteLine();

        var profileName =
            SupportToolkitProfileNames.Normalize(
                ReadWithDefault(
                    "Profile name",
                    defaultProfileName
                )
            );

        var existingProfile =
            FindProfile(
                configuration,
                profileName
            );

        var profile =
            existingProfile
            ?? new SupportToolkitProfile();

        Console.WriteLine();

        Console.WriteLine(
            "Acronis"
        );

        Console.WriteLine(
            "-------"
        );

        profile.Acronis.DatacenterUrl =
            ReadRequiredWithDefault(
                "Datacenter URL",
                profile.Acronis.DatacenterUrl
            );

        profile.Acronis.ClientId =
            ReadRequiredWithDefault(
                "Client ID",
                profile.Acronis.ClientId
            );

        var acronisSecretKey =
            SupportToolkitSecretKeys
                .AcronisClientSecret(
                    profileName
                );

        var acronisSecretExists =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    acronisSecretKey
                )
            );

        var acronisSecret =
            ReadSecret(
                "Client secret",
                acronisSecretExists
            );

        Console.WriteLine();

        Console.WriteLine(
            "Zendesk"
        );

        Console.WriteLine(
            "-------"
        );

        profile.Zendesk.Subdomain =
            ReadRequiredWithDefault(
                "Subdomain",
                profile.Zendesk.Subdomain
            );

        profile.Zendesk.ClientId =
            ReadRequiredWithDefault(
                "Client ID",
                profile.Zendesk.ClientId
            );

        var zendeskSecretKey =
            SupportToolkitSecretKeys
                .ZendeskClientSecret(
                    profileName
                );

        var zendeskSecretExists =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    zendeskSecretKey
                )
            );

        var zendeskSecret =
            ReadSecret(
                "Client secret",
                zendeskSecretExists
            );

        Console.WriteLine();

        Console.WriteLine(
            "Runtime"
        );

        Console.WriteLine(
            "-------"
        );

        profile.Modules[
            "backup-aggregator"
        ] =
            ReadMode(
                "Backup Aggregator",
                GetConfiguredMode(
                    profile,
                    "backup-aggregator"
                )
            );

        profile.Modules[
            "backup-health"
        ] =
            ReadMode(
                "Backup Health",
                GetConfiguredMode(
                    profile,
                    "backup-health"
                )
            );

        profile.Modules[
            "ticketing"
        ] =
            ReadMode(
                "Ticketing",
                GetConfiguredMode(
                    profile,
                    "ticketing"
                )
            );

        configuration.ActiveProfile =
            profileName;

        RemoveProfileIgnoringCase(
            configuration,
            profileName
        );

        configuration.Profiles[
            profileName
        ] =
            profile;

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

        await _configurationStore.SaveAsync(
            configuration
        );

        if (!string.IsNullOrEmpty(
                acronisSecret))
        {
            await _secretStore.SetSecretAsync(
                acronisSecretKey,
                acronisSecret
            );
        }

        if (!string.IsNullOrEmpty(
                zendeskSecret))
        {
            await _secretStore.SetSecretAsync(
                zendeskSecretKey,
                zendeskSecret
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Configuration saved."
        );

        Console.WriteLine(
            $"Active profile: {profileName}"
        );

        Console.WriteLine(
            $"Configuration file: {_configurationStore.ConfigurationPath}"
        );

        Console.WriteLine();

        if (!acronisSecretExists
            && string.IsNullOrEmpty(
                acronisSecret))
        {
            Console.WriteLine(
                "WARNING: Acronis client secret is not configured."
            );
        }

        if (!zendeskSecretExists
            && string.IsNullOrEmpty(
                zendeskSecret))
        {
            Console.WriteLine(
                "WARNING: Zendesk client secret is not configured."
            );
        }

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

        if (configuration.Profiles.Count == 0)
        {
            Console.WriteLine(
                "Status: not configured"
            );

            return 0;
        }

        var profileName =
            SupportToolkitProfileNames.Normalize(
                configuration.ActiveProfile
            );

        var profile =
            FindProfile(
                configuration,
                profileName
            );

        if (profile is null)
        {
            Console.WriteLine(
                $"Active profile '{profileName}' does not exist."
            );

            return 1;
        }

        var acronisSecretConfigured =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    SupportToolkitSecretKeys
                        .AcronisClientSecret(
                            profileName
                        )
                )
            );

        var zendeskSecretConfigured =
            !string.IsNullOrWhiteSpace(
                await _secretStore.GetSecretAsync(
                    SupportToolkitSecretKeys
                        .ZendeskClientSecret(
                            profileName
                        )
                )
            );

        Console.WriteLine(
            $"Active profile: {profileName}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Acronis"
        );

        Console.WriteLine(
            $"  Datacenter URL: {DisplayValue(profile.Acronis.DatacenterUrl)}"
        );

        Console.WriteLine(
            $"  Client ID:      {DisplayValue(profile.Acronis.ClientId)}"
        );

        Console.WriteLine(
            $"  Client secret:  {DisplaySecretStatus(acronisSecretConfigured)}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Zendesk"
        );

        Console.WriteLine(
            $"  Subdomain:      {DisplayValue(profile.Zendesk.Subdomain)}"
        );

        Console.WriteLine(
            $"  Client ID:      {DisplayValue(profile.Zendesk.ClientId)}"
        );

        Console.WriteLine(
            $"  Client secret:  {DisplaySecretStatus(zendeskSecretConfigured)}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Runtime"
        );

        Console.WriteLine(
            $"  Backup Aggregator: " +
            $"{GetConfiguredMode(profile, "backup-aggregator").ToString().ToLowerInvariant()}"
        );

        Console.WriteLine(
            $"  Backup Health:     " +
            $"{GetConfiguredMode(profile, "backup-health").ToString().ToLowerInvariant()}"
        );

        Console.WriteLine(
            $"  Ticketing:         " +
            $"{GetConfiguredMode(profile, "ticketing").ToString().ToLowerInvariant()}"
        );

        return 0;
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
                $"[{defaultMode.ToString().ToLowerInvariant()}]: "
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

    private static void RemoveProfileIgnoringCase(
        SupportToolkitConfiguration configuration,
        string profileName)
    {
        var existingKey =
            configuration.Profiles.Keys
                .FirstOrDefault(
                    key =>
                        string.Equals(
                            key,
                            profileName,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

        if (existingKey is not null)
        {
            configuration.Profiles.Remove(
                existingKey
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

    private static string DisplaySecretStatus(
        bool configured)
    {
        return configured
            ? "configured"
            : "missing";
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
            "  SupportToolkit configure status"
        );

        Console.WriteLine();

        Console.WriteLine(
            "The configuration file contains only non-secret settings."
        );

        Console.WriteLine(
            "Authentication secrets are stored separately in the " +
            "configured secret store."
        );
    }
}