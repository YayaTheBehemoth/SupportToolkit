
using System.Globalization;
using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    private static void InvalidateZendeskDestinationsIfInstanceChanged(
        ZendeskConnectionConfiguration connection,
        SupportToolkitProfile profile,
        string previousSubdomain)
    {
        if (string.Equals(
            previousSubdomain,
            connection.Subdomain,
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (connection.Destinations.Count > 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                "WARNING: Zendesk subdomain changed."
            );

            Console.WriteLine(
                "Existing group destinations have been cleared because " +
                "group IDs belong to the previous Zendesk instance."
            );

            Console.WriteLine(
                "Other profiles sharing this connection may need " +
                "their destinations reviewed."
            );

            connection.Destinations.Clear();
        }

        profile.ZendeskDestination = null;
    }

    private static void ConfigureZendeskDestinations(
        ZendeskConnectionConfiguration connection,
        SupportToolkitProfile profile)
    {
        Console.WriteLine();

        Console.WriteLine(
            "Zendesk ticket destinations"
        );

        Console.WriteLine(
            "---------------------------"
        );

        Console.WriteLine(
            "Each destination routes tickets to one Zendesk group."
        );

        Console.WriteLine(
            "Group IDs must belong to the selected Zendesk instance."
        );

        while (true)
        {
            Console.WriteLine();

            WriteZendeskDestinations(
                connection
            );

            if (connection.Destinations.Count > 0
                && !ReadConfirmation(
                    "Add or update a destination? [y/N]: ",
                    defaultValue: false
                ))
            {
                break;
            }

            if (connection.Destinations.Count == 0)
            {
                Console.WriteLine(
                    "At least one destination is required."
                );
            }

            var destinationName =
                ReadZendeskDestinationName();

            var existingDestination =
                FindZendeskDestination(
                    connection,
                    destinationName
                );

            var groupId =
                ReadZendeskGroupId(
                    existingDestination?.GroupId ?? 0
                );

            var duplicate =
                connection.Destinations
                    .FirstOrDefault(
                        pair =>
                            !string.Equals(
                                pair.Key,
                                destinationName,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && pair.Value.GroupId == groupId
                    );

            if (!string.IsNullOrWhiteSpace(
                duplicate.Key))
            {
                Console.WriteLine();

                Console.WriteLine(
                    $"Group ID {groupId} is already configured as " +
                    $"destination '{duplicate.Key}'."
                );

                Console.WriteLine(
                    "Different destinations must use different groups."
                );

                continue;
            }

            RemoveKeyIgnoringCase(
                connection.Destinations,
                destinationName
            );

            connection.Destinations[
                destinationName
            ] =
                new ZendeskDestinationConfiguration
                {
                    GroupId = groupId
                };

            Console.WriteLine();

            Console.WriteLine(
                $"Destination '{destinationName}' saved in memory " +
                $"with group ID {groupId}."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Select this profile's ticket destination"
        );

        Console.WriteLine(
            "----------------------------------------"
        );

        profile.ZendeskDestination =
            ReadSelectedZendeskDestination(
                connection,
                profile.ZendeskDestination
            );

        var selected =
            FindZendeskDestination(
                connection,
                profile.ZendeskDestination
            )!;

        Console.WriteLine();

        Console.WriteLine(
            $"Selected destination: {profile.ZendeskDestination}"
        );

        Console.WriteLine(
            $"Selected group ID:    {selected.GroupId}"
        );
    }

    private static void WriteZendeskDestinations(
        ZendeskConnectionConfiguration connection)
    {
        if (connection.Destinations.Count == 0)
        {
            Console.WriteLine(
                "No destinations configured."
            );

            return;
        }

        Console.WriteLine(
            "Configured destinations:"
        );

        foreach (var pair in connection.Destinations
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase
                     ))
        {
            Console.WriteLine(
                $"  {pair.Key}: group {pair.Value.GroupId}"
            );
        }
    }

    private static string ReadZendeskDestinationName()
    {
        while (true)
        {
            var value =
                ReadRequiredWithDefault(
                    "Destination name",
                    string.Empty
                );

            try
            {
                return SupportToolkitConfigurationNames
                    .NormalizeConnectionName(
                        value
                    );
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine(
                    exception.Message
                );
            }
        }
    }

    private static long ReadZendeskGroupId(
        long existingGroupId)
    {
        while (true)
        {
            Console.Write(
                existingGroupId > 0
                    ? $"Zendesk group ID [{existingGroupId}]: "
                    : "Zendesk group ID: "
            );

            var input =
                Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)
                && existingGroupId > 0)
            {
                return existingGroupId;
            }

            if (long.TryParse(
                    input?.Trim(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var groupId
                )
                && groupId > 0)
            {
                return groupId;
            }

            Console.WriteLine(
                "Enter a positive numeric Zendesk group ID."
            );
        }
    }

    private static string ReadSelectedZendeskDestination(
        ZendeskConnectionConfiguration connection,
        string? currentDestination)
    {
        var configuredDefault =
            connection.Destinations.Keys
                .FirstOrDefault(
                    name =>
                        string.Equals(
                            name,
                            currentDestination,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

        var defaultName =
            configuredDefault
            ?? GetDefaultConnectionName(
                null,
                connection.Destinations.Keys
            );

        while (true)
        {
            var input =
                ReadRequiredWithDefault(
                    "Destination name",
                    defaultName
                );

            string destinationName;

            try
            {
                destinationName =
                    SupportToolkitConfigurationNames
                        .NormalizeConnectionName(
                            input
                        );
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine(
                    exception.Message
                );

                continue;
            }

            var destination =
                FindZendeskDestination(
                    connection,
                    destinationName
                );

            if (destination is null)
            {
                Console.WriteLine(
                    $"Unknown Zendesk destination '{destinationName}'."
                );

                continue;
            }

            if (destination.GroupId <= 0)
            {
                Console.WriteLine(
                    $"Destination '{destinationName}' has an invalid " +
                    "group ID. Update that destination first."
                );

                continue;
            }

            return destinationName;
        }
    }

    private static ZendeskDestinationConfiguration?
        FindZendeskDestination(
            ZendeskConnectionConfiguration connection,
            string destinationName)
    {
        return connection.Destinations
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        destinationName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }
}
