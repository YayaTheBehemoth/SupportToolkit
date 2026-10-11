using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
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
}
