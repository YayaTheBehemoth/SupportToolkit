using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
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
}
