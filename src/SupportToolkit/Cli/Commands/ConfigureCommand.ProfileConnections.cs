
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
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
}
