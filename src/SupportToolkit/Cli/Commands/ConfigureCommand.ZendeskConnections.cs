
using System.Globalization;
using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private static (string ConnectionName, bool IsNew)
        SelectZendeskConnection(
            SupportToolkitConfiguration configuration,
            SupportToolkitProfile profile)
    {
        var existingConnections =
            configuration.Connections.Zendesk
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (existingConnections.Count == 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                "No Zendesk connections exist yet."
            );

            return ReadNewZendeskConnectionName(
                configuration
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Existing Zendesk connections:"
        );

        for (var index = 0;
             index < existingConnections.Count;
             index++)
        {
            var pair =
                existingConnections[index];

            Console.WriteLine(
                $"  {index + 1}. {pair.Key} " +
                $"({DisplayValue(pair.Value.Subdomain)})"
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "  N. Create a new Zendesk connection"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Select an existing connection to reuse its credentials."
        );

        Console.WriteLine(
            "Use N only when connecting to a new Zendesk instance " +
            "or deliberately creating separate credentials."
        );

        var defaultIndex =
            -1;

        if (!string.IsNullOrWhiteSpace(
            profile.ZendeskConnection))
        {
            defaultIndex =
                existingConnections.FindIndex(
                    pair =>
                        string.Equals(
                            pair.Key,
                            profile.ZendeskConnection,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
        }

        /*
         * A single existing connection is a safe default for a new
         * profile. With multiple connections, an explicit selection
         * is required unless the profile already references one.
         */
        if (defaultIndex < 0
            && existingConnections.Count == 1)
        {
            defaultIndex = 0;
        }

        while (true)
        {
            Console.Write(
                defaultIndex >= 0
                    ? $"Select Zendesk connection [{defaultIndex + 1}]: "
                    : "Select Zendesk connection (number or N): "
            );

            var input =
                Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(input)
                && defaultIndex >= 0)
            {
                return (
                    existingConnections[defaultIndex].Key,
                    false
                );
            }

            if (string.Equals(
                    input,
                    "n",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    input,
                    "new",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ReadNewZendeskConnectionName(
                    configuration
                );
            }

            if (int.TryParse(
                    input,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var selectedIndex
                )
                && selectedIndex >= 1
                && selectedIndex <= existingConnections.Count)
            {
                return (
                    existingConnections[selectedIndex - 1].Key,
                    false
                );
            }

            Console.WriteLine(
                "Choose an existing connection by number, " +
                "or enter N to create a new connection."
            );
        }
    }

    private static (string ConnectionName, bool IsNew)
        ReadNewZendeskConnectionName(
            SupportToolkitConfiguration configuration)
    {
        Console.WriteLine();

        while (true)
        {
            var name =
                ReadRequiredWithDefault(
                    "New Zendesk connection name",
                    string.Empty
                );

            string normalizedName;

            try
            {
                normalizedName =
                    SupportToolkitConfigurationNames
                        .NormalizeConnectionName(
                            name
                        );
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine(
                    exception.Message
                );

                continue;
            }

            if (FindZendeskConnection(
                    configuration,
                    normalizedName
                ) is not null)
            {
                Console.WriteLine(
                    $"Connection '{normalizedName}' already exists."
                );

                Console.WriteLine(
                    "Select it from the existing connection list " +
                    "instead of creating it again."
                );

                continue;
            }

            return (
                normalizedName,
                true
            );
        }
    }
}
