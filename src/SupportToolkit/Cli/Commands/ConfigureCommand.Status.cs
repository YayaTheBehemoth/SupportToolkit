
using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private static readonly string[] RuntimeEnvironmentVariables =
    [
        "SUPPORTTOOLKIT_CONFIG",
        "SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE",
        "SUPPORTTOOLKIT_BACKUP_HEALTH_MODE",
        "ACRONIS_DATACENTER_URL",
        "ACRONIS_CLIENT_ID",
        "ACRONIS_CLIENT_SECRET",
        "ZENDESK_SUBDOMAIN",
        "ZENDESK_CLIENT_ID",
        "ZENDESK_CLIENT_SECRET"
    ];

    private async Task<int> WriteStatusAsync()
    {
        var configuration =
            await _configurationStore.LoadAsync();

        Console.WriteLine("SupportToolkit configuration");
        Console.WriteLine("============================");
        Console.WriteLine();

        Console.WriteLine(
            $"Configuration file: {_configurationStore.ConfigurationPath}");

        Console.WriteLine(
            $"Active profile:     {configuration.ActiveProfile}");

        Console.WriteLine();

        await WriteEffectiveRuntimeAsync(configuration);

        WriteSavedProfiles(configuration);

        await WriteSavedConnectionsAsync(configuration);

        return 0;
    }

    private async Task WriteEffectiveRuntimeAsync(
        SupportToolkitConfiguration configuration)
    {
        Console.WriteLine("Effective runtime");
        Console.WriteLine("-----------------");
        Console.WriteLine();

        var profile =
            FindProfile(
                configuration,
                configuration.ActiveProfile);

        if (profile is null)
        {
            Console.WriteLine(
                "No valid active profile is selected.");

            Console.WriteLine(
                "Select a profile with: SupportToolkit configure use <name>");

            Console.WriteLine();

            WriteEnvironmentOverrides();
            return;
        }

        var resolver =
            new SupportToolkitConfigurationResolver(
                _configurationStore,
                _secretStore);

        var aggregatorMode =
            await WriteEffectiveModeAsync(
                resolver,
                BackupAggregatorCommand,
                "Backup Aggregator");

        var healthMode =
            await WriteEffectiveModeAsync(
                resolver,
                BackupHealthCommand,
                "Backup Health");

        Console.WriteLine();

        await WriteEffectiveAcronisAsync(
            profile,
            resolver,
            aggregatorMode == SupportToolkitMode.Production
                || healthMode == SupportToolkitMode.Production);

        Console.WriteLine();

        await WriteEffectiveZendeskAsync(
            profile,
            resolver);

        Console.WriteLine();

        WriteEnvironmentOverrides();

        Console.WriteLine(
            "Configuration readiness is checked locally.");

        Console.WriteLine(
            "No provider authentication or API requests were performed.");

        Console.WriteLine();
    }

    private static async Task<SupportToolkitMode?>
        WriteEffectiveModeAsync(
            SupportToolkitConfigurationResolver resolver,
            string moduleCommand,
            string displayName)
    {
        var environmentVariable =
            SupportToolkitRuntimeOptions
                .GetEnvironmentVariableName(moduleCommand);

        var overridden =
            IsEnvironmentVariableSet(environmentVariable);

        try
        {
            var mode =
                await resolver.GetModuleModeAsync(moduleCommand);

            var source =
                overridden
                    ? $" (overridden by {environmentVariable})"
                    : " (active profile/default)";

            Console.WriteLine(
                $"{displayName}: {FormatMode(mode)}{source}");

            return mode;
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(
                $"{displayName}: invalid configuration");

            Console.WriteLine(
                $"  {exception.Message}");

            return null;
        }
    }

    private static async Task WriteEffectiveAcronisAsync(
        SupportToolkitProfile profile,
        SupportToolkitConfigurationResolver resolver,
        bool productionRequired)
    {
        Console.WriteLine("Acronis");

        var completeEnvironmentConnection =
            IsEnvironmentVariableSet("ACRONIS_DATACENTER_URL")
            && IsEnvironmentVariableSet("ACRONIS_CLIENT_ID")
            && IsEnvironmentVariableSet("ACRONIS_CLIENT_SECRET");

        var anyEnvironmentConnection =
            IsEnvironmentVariableSet("ACRONIS_DATACENTER_URL")
            || IsEnvironmentVariableSet("ACRONIS_CLIENT_ID")
            || IsEnvironmentVariableSet("ACRONIS_CLIENT_SECRET");

        if (!completeEnvironmentConnection
            && !anyEnvironmentConnection
            && string.IsNullOrWhiteSpace(profile.AcronisConnection))
        {
            Console.WriteLine(
                "  Connection: not selected");

            if (productionRequired)
            {
                Console.WriteLine(
                    "  WARNING: Production backup modules require " +
                    "an Acronis connection.");
            }

            return;
        }

        Console.WriteLine(
            completeEnvironmentConnection
                ? "  Connection: environment-only"
                : $"  Connection: {DisplayOptionalValue(profile.AcronisConnection)}");

        try
        {
            var resolved =
                await resolver.GetAcronisConnectionAsync();

            // Validate the effective settings using the same
            // provider options as the production workflow.
            _ = AcronisOptions.Create(
                resolved.DatacenterUrl,
                resolved.ClientId,
                resolved.ClientSecret);

            Console.WriteLine(
                $"  Datacenter: {FormatEndpoint(resolved.DatacenterUrl)}");

            Console.WriteLine(
                "  Configuration: valid locally");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(
                "  Configuration: incomplete or invalid");

            Console.WriteLine(
                $"  Reason: {exception.Message}");
        }
    }

    private static async Task WriteEffectiveZendeskAsync(
        SupportToolkitProfile profile,
        SupportToolkitConfigurationResolver resolver)
    {
        Console.WriteLine("Zendesk ticket submission");

        if (string.IsNullOrWhiteSpace(profile.ZendeskConnection))
        {
            Console.WriteLine(
                "  Connection: not selected");

            Console.WriteLine(
                "  Submission: unavailable until a destination is configured.");

            return;
        }

        Console.WriteLine(
            $"  Connection alias: {profile.ZendeskConnection}");

        Console.WriteLine(
            $"  Destination alias: {DisplayOptionalValue(profile.ZendeskDestination)}");

        try
        {
            /*
             * Reuse the actual runtime configuration resolver.
             *
             * This validates:
             * - The active profile's connection reference.
             * - The selected destination and group ID.
             * - The Zendesk instance override restriction.
             * - Credential availability.
             *
             * This does not authenticate or submit a ticket.
             */
            var resolved =
                await resolver.GetZendeskConnectionAsync();

            var options =
                ZendeskOptions.Create(
                    resolved.Subdomain,
                    resolved.ClientId,
                    resolved.ClientSecret);

            Console.WriteLine(
                $"  Instance: {options.BaseUri.GetLeftPart(UriPartial.Authority)}");

            Console.WriteLine(
                $"  Destination: {resolved.DestinationName}");

            Console.WriteLine(
                $"  Group ID: {resolved.GroupId}");

            Console.WriteLine(
                $"  Submitter user ID: {resolved.SubmitterId}");

            Console.WriteLine(
                $"  Requester user ID: {resolved.RequesterId}");

            Console.WriteLine(
                "  Configuration: valid locally");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(
                "  Configuration: incomplete or invalid");

            Console.WriteLine(
                $"  Reason: {exception.Message}");

            Console.WriteLine(
                "  Ticket submission will be unavailable " +
                "until the configuration is corrected.");
        }
    }

    private static void WriteEnvironmentOverrides()
    {
        Console.WriteLine("Environment overrides");
        Console.WriteLine("---------------------");

        var found = false;

        foreach (var variable in RuntimeEnvironmentVariables)
        {
            if (!IsEnvironmentVariableSet(variable))
            {
                continue;
            }

            found = true;

            Console.WriteLine(
                $"  {variable}: set (value hidden)");
        }

        if (!found)
        {
            Console.WriteLine("  None");
        }

        /*
         * The obsolete flag is intentionally not part of the
         * runtime resolver anymore.
         */
        if (IsEnvironmentVariableSet(
                "SUPPORTTOOLKIT_ALLOW_WRITES"))
        {
            Console.WriteLine();

            Console.WriteLine(
                "  NOTE: SUPPORTTOOLKIT_ALLOW_WRITES is obsolete " +
                "and ignored. Remove it from your environment.");
        }

        Console.WriteLine();
    }

    private static bool IsEnvironmentVariableSet(
        string variableName)
    {
        return !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(variableName));
    }

    private static string FormatEndpoint(string? value)
    {
        if (Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri)
            && uri.Scheme == Uri.UriSchemeHttps)
        {
            /*
             * Display only the origin rather than potentially
             * sensitive URL paths or query parameters.
             */
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return "invalid or missing URL";
    }

    private static void WriteSavedProfiles(
        SupportToolkitConfiguration configuration)
    {
        Console.WriteLine("Saved profiles");
        Console.WriteLine("--------------");
        Console.WriteLine();

        if (configuration.Profiles.Count == 0)
        {
            Console.WriteLine("No profiles configured.");
            Console.WriteLine();
            return;
        }

        foreach (var pair in configuration.Profiles
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            var marker =
                string.Equals(
                    pair.Key,
                    configuration.ActiveProfile,
                    StringComparison.OrdinalIgnoreCase)
                    ? "*"
                    : " ";

            var profile = pair.Value;

            Console.WriteLine($"{marker} {pair.Key}");

            Console.WriteLine(
                $"    Acronis connection:  " +
                $"{DisplayOptionalValue(profile.AcronisConnection)}");

            Console.WriteLine(
                $"    Zendesk connection:  " +
                $"{DisplayOptionalValue(profile.ZendeskConnection)}");

            Console.WriteLine(
                $"    Zendesk destination: " +
                $"{DisplayOptionalValue(profile.ZendeskDestination)}");

            Console.WriteLine(
                $"    Backup Aggregator:   " +
                $"{FormatMode(GetConfiguredMode(
                    profile,
                    BackupAggregatorCommand))}");

            Console.WriteLine(
                $"    Backup Health:       " +
                $"{FormatMode(GetConfiguredMode(
                    profile,
                    BackupHealthCommand))}");
        }

        Console.WriteLine();
    }

    private async Task WriteSavedConnectionsAsync(
        SupportToolkitConfiguration configuration)
    {
        Console.WriteLine("Saved Acronis connections");
        Console.WriteLine("-------------------------");
        Console.WriteLine();

        if (configuration.Connections.Acronis.Count == 0)
        {
            Console.WriteLine("No Acronis connections configured.");
        }
        else
        {
            foreach (var pair in configuration.Connections.Acronis
                         .OrderBy(
                             pair => pair.Key,
                             StringComparer.OrdinalIgnoreCase))
            {
                var secretConfigured =
                    !string.IsNullOrWhiteSpace(
                        await _secretStore.GetSecretAsync(
                            SupportToolkitSecretKeys
                                .AcronisClientSecret(pair.Key)));

                Console.WriteLine(pair.Key);

                Console.WriteLine(
                    $"    Datacenter:    {FormatEndpoint(pair.Value.DatacenterUrl)}");

                Console.WriteLine(
                    $"    Client ID:     {DisplaySecretStatus(
                        !string.IsNullOrWhiteSpace(pair.Value.ClientId))}");

                Console.WriteLine(
                    $"    Client secret: {DisplaySecretStatus(secretConfigured)}");
            }
        }

        Console.WriteLine();

        Console.WriteLine("Saved Zendesk connections");
        Console.WriteLine("-------------------------");
        Console.WriteLine();

        if (configuration.Connections.Zendesk.Count == 0)
        {
            Console.WriteLine("No Zendesk connections configured.");
        }
        else
        {
            foreach (var pair in configuration.Connections.Zendesk
                         .OrderBy(
                             pair => pair.Key,
                             StringComparer.OrdinalIgnoreCase))
            {
                var connection = pair.Value;

                var secretConfigured =
                    !string.IsNullOrWhiteSpace(
                        await _secretStore.GetSecretAsync(
                            SupportToolkitSecretKeys
                                .ZendeskClientSecret(pair.Key)));

                Console.WriteLine(pair.Key);

                Console.WriteLine(
                    $"    Instance:      {DisplayValue(connection.Subdomain)}");

                Console.WriteLine(
                    $"    Client ID:     {DisplaySecretStatus(
                        !string.IsNullOrWhiteSpace(connection.ClientId))}");

                Console.WriteLine(
                    $"    Client secret: {DisplaySecretStatus(secretConfigured)}");

                Console.WriteLine(
                    $"    Submitter ID:  {DisplayZendeskUserId(connection.SubmitterId)}");

                Console.WriteLine(
                    $"    Requester ID:  {DisplayZendeskUserId(connection.RequesterId)}");

                if (connection.Destinations.Count == 0)
                {
                    Console.WriteLine(
                        "    Destinations:  none");

                    continue;
                }

                Console.WriteLine("    Destinations:");

                foreach (var destination in connection.Destinations
                             .OrderBy(
                                 item => item.Key,
                                 StringComparer.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        $"      {destination.Key}: " +
                        $"group {destination.Value.GroupId}");
                }
            }
        }

        Console.WriteLine();
    }

    private static string DisplayZendeskUserId(
        long? userId)
    {
        return userId switch
        {
            null => "not configured (required for ticket submission)",
            <= 0 => $"{userId} (invalid; must be positive)",
            _ => userId.Value.ToString(CultureInfo.InvariantCulture)
        };
    }
}
