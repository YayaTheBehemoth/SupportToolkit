
using SupportToolkit.Core.Configuration;

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

        if (activateProfile || isAlreadyActive)
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
