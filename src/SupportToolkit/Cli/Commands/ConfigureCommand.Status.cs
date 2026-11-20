
using System.Globalization;
using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
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

        Console.WriteLine(
            $"Secrets:            {_secretStore.Description}");

        foreach (var clash in SecretLocationClashes.Find(configuration, _secretStore, ProviderConnectionsComposition.All))
        {
            Console.WriteLine(
                $"  WARNING: {clash.Describe()}");
        }

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

        var usageReportMode =
            await WriteEffectiveModeAsync(
                resolver,
                UsageReportModule.ModuleCommand,
                "Usage report");

        await WriteUsageReportItemsAsync(resolver);

        Console.WriteLine();

        var backup = BackupSelection.For(profile);

        await WriteEffectiveAcronisAsync(
            profile,
            resolver,
            (aggregatorMode == SupportToolkitMode.Production
             && BackupSystemsComposition.ReviewsAcronis(backup))
            || usageReportMode == SupportToolkitMode.Production);

        if (!BackupSystemsComposition.ReviewsAcronis(backup))
        {
            Console.WriteLine();

            await WriteEffectiveBackupSystemAsync(backup!, resolver);
        }

        Console.WriteLine();

        await WriteEffectiveTicketingAsync(
            profile,
            resolver);

        Console.WriteLine();

        await WriteEffectiveZendeskDeletionAsync(
            profile,
            resolver);

        Console.WriteLine();

        await WriteEffectiveBusinessCentralAsync(
            profile,
            resolver);

        Console.WriteLine();

        var commercial = CommercialSelection.For(profile);

        if (!CommercialSystemsComposition.ReadsBusinessCentral(commercial))
        {
            Console.WriteLine(
                $"{CommercialSystemsComposition.SystemName(commercial!)} monitoring scope");

            Console.WriteLine(
                $"  Connection alias: {DisplayOptionalValue(commercial!.Connection)}");

            try
            {
                foreach (var detail in await CommercialSystemsComposition.DescribeAsync(resolver))
                {
                    Console.WriteLine($"  {detail.Label}: {detail.Value}");
                }

                Console.WriteLine("  Configuration: valid locally");
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine("  Configuration: incomplete or invalid");
                Console.WriteLine($"  Reason: {exception.Message}");
            }

            Console.WriteLine();
        }

        await WriteEffectiveReviewWaitAsync(resolver);

        Console.WriteLine();

        WriteEnvironmentOverrides();

        Console.WriteLine(
            "Configuration readiness is checked locally.");

        Console.WriteLine(
            "No provider authentication or API requests were performed.");

        Console.WriteLine();
    }

    private static async Task WriteUsageReportItemsAsync(
        SupportToolkitConfigurationResolver resolver)
    {
        try
        {
            var items =
                await resolver.GetServiceItemNumbersAsync(
                    UsageReportModule.ModuleCommand);

            Console.WriteLine(
                "  Usage report backup items: " +
                (items.Count == 0
                    ? "none (needed in production mode)"
                    : string.Join(", ", items)));
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine("  Usage report backup items: invalid configuration");
            Console.WriteLine($"  Reason: {exception.Message}");
        }
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

    /// <summary>
    /// Ticket submission through whichever system the profile selects,
    /// resolved by the same code a submission uses. This validates the
    /// selection, destination, routing and credential availability; it
    /// does not authenticate or submit a ticket.
    /// </summary>
    private static async Task WriteEffectiveTicketingAsync(
        SupportToolkitProfile profile,
        SupportToolkitConfigurationResolver resolver)
    {
        var selection = TicketingSelection.For(profile);

        if (selection is null)
        {
            Console.WriteLine("Ticket submission");

            Console.WriteLine(
                "  Connection: not selected");

            Console.WriteLine(
                "  Submission: unavailable until a destination is configured.");

            return;
        }

        Console.WriteLine($"{TicketingComposition.SystemName(selection)} ticket submission");

        Console.WriteLine(
            $"  Connection alias: {DisplayOptionalValue(selection.Connection)}");

        Console.WriteLine(
            $"  Destination alias: {DisplayOptionalValue(selection.Destination)}");

        try
        {
            var target =
                await TicketingComposition.ResolveTargetAsync(resolver);

            foreach (var detail in target.Details)
            {
                Console.WriteLine($"  {detail.Label}: {detail.Value}");
            }

            Console.WriteLine(
                "  Configuration: valid locally");

            if (target.Destination.Caveat is { } caveat)
            {
                Console.WriteLine($"  Note: {caveat}");
            }
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

    /// <summary>
    /// The backup system the review reads, when it is not Acronis.
    /// Resolved by the same code a review uses; nothing is called.
    /// </summary>
    private static async Task WriteEffectiveBackupSystemAsync(
        BackupSelection selection,
        SupportToolkitConfigurationResolver resolver)
    {
        Console.WriteLine(
            $"{BackupSystemsComposition.SystemName(selection)} backup review");

        Console.WriteLine(
            $"  Connection alias: {DisplayOptionalValue(selection.Connection)}");

        try
        {
            foreach (var detail in
                     await BackupSystemsComposition.DescribeAsync(resolver, selection))
            {
                Console.WriteLine($"  {detail.Label}: {detail.Value}");
            }

            Console.WriteLine("  Configuration: valid locally");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine("  Configuration: incomplete or invalid");
            Console.WriteLine($"  Reason: {exception.Message}");
        }
    }

    private static void WriteEnvironmentOverrides()
    {
        Console.WriteLine("Environment overrides");
        Console.WriteLine("---------------------");

        var found = false;

        foreach (var variable in RuntimeEnvironmentVariables.Overrides)
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
         * Obsolete variables are intentionally not part of the
         * runtime resolver anymore.
         */
        foreach (var obsolete in RuntimeEnvironmentVariables.Obsolete)
        {
            if (!IsEnvironmentVariableSet(obsolete))
            {
                continue;
            }

            Console.WriteLine();

            Console.WriteLine(
                $"  NOTE: {obsolete} is obsolete " +
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

            var ticketing = TicketingSelection.For(profile);

            Console.WriteLine(
                $"    Ticketing:           " +
                (ticketing is null
                    ? DisplayOptionalValue(null)
                    : $"{TicketingComposition.SystemName(ticketing)}, connection " +
                      $"{DisplayOptionalValue(ticketing.Connection)}, " +
                      $"destination {DisplayOptionalValue(ticketing.Destination)}"));

            Console.WriteLine(
                $"    Zendesk deletion:    " +
                $"{DisplayOptionalValue(profile.ZendeskDeletionConnection)}");

            Console.WriteLine(
                $"    Business Central:    " +
                $"{DisplayOptionalValue(profile.BusinessCentralConnection)}");

            Console.WriteLine(
                $"    Backup Aggregator:   " +
                $"{FormatMode(GetConfiguredMode(
                    profile,
                    BackupAggregatorCommand))}");
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
                    $"    Client secret: {SecretStatusText.Describe(
                        _secretStore,
                        SupportToolkitSecretKeys.AcronisClientSecret(pair.Key),
                        secretConfigured,
                        "configured",
                        "missing")}");
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
                    $"    Client secret: {SecretStatusText.Describe(
                        _secretStore,
                        SupportToolkitSecretKeys.ZendeskClientSecret(pair.Key),
                        secretConfigured,
                        "configured",
                        "missing")}");

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

        WriteSavedZendeskDeletionConnections(configuration);
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

    private static async Task WriteEffectiveReviewWaitAsync(
        SupportToolkitConfigurationResolver resolver)
    {
        Console.WriteLine(
            "Review wait for in-progress backups (automatic runs only)");
        Console.WriteLine(
            "  Manual runs never wait; they report running backups as " +
            "not final.");

        try
        {
            var settings =
                await resolver.GetInProgressWaitSettingsAsync(
                    BackupAggregatorCommand);

            Console.WriteLine(
                settings.MaxWaitSeconds == 0
                    ? "  Waiting: disabled"
                    : $"  Waiting: up to {settings.MaxWaitSeconds}s, " +
                      $"checking every {settings.PollIntervalSeconds}s");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine("  Waiting: invalid setting");
            Console.WriteLine($"  Reason: {exception.Message}");
        }
    }
}
