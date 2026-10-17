
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

            /*
             * A connection may contain destinations with invalid group
             * IDs, for example from an old or manually edited config.
             *
             * Do not exit this step unless there is at least one
             * selectable destination.
             */
            var hasValidDestination =
                connection.Destinations.Values.Any(
                    destination =>
                        destination is not null
                        && destination.GroupId > 0
                );

            if (hasValidDestination
                && !ReadConfirmation(
                    "Add or update a destination? [y/N]: ",
                    defaultValue: false
                ))
            {
                break;
            }

            if (!hasValidDestination)
            {
                Console.WriteLine(
                    "At least one valid destination is required."
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
                            && pair.Value is not null
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
            if (pair.Value is null
                || pair.Value.GroupId <= 0)
            {
                Console.WriteLine(
                    $"  {pair.Key}: invalid group ID"
                );

                continue;
            }

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

    /// <summary>
    /// Presents the valid destinations as a numbered list.
    ///
    /// The profile's existing selection is the default when valid.
    /// A sole valid destination is also a safe default.
    ///
    /// With multiple destinations and no existing selection,
    /// the operator must make an explicit choice.
    ///
    /// Destination names remain accepted for compatibility.
    /// </summary>
    private static string ReadSelectedZendeskDestination(
        ZendeskConnectionConfiguration connection,
        string? currentDestination)
    {
        var availableDestinations =
            connection.Destinations
                .Where(
                    pair =>
                        pair.Value is not null
                        && pair.Value.GroupId > 0
                )
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (availableDestinations.Count == 0)
        {
            throw new InvalidOperationException(
                "No valid Zendesk destinations are available. " +
                "Configure a destination with a positive group ID."
            );
        }

        Console.WriteLine();

        Console.WriteLine(
            "Available destinations:"
        );

        for (var index = 0;
             index < availableDestinations.Count;
             index++)
        {
            var pair =
                availableDestinations[index];

            Console.WriteLine(
                $"  {index + 1}. {pair.Key} " +
                $"(group {pair.Value.GroupId})"
            );
        }

        var invalidCount =
            connection.Destinations.Count
            - availableDestinations.Count;

        if (invalidCount > 0)
        {
            Console.WriteLine();

            Console.WriteLine(
                $"{invalidCount} invalid destination(s) excluded. " +
                "Update their group IDs before selecting them."
            );
        }

        var defaultIndex =
            -1;

        if (!string.IsNullOrWhiteSpace(
                currentDestination))
        {
            defaultIndex =
                availableDestinations.FindIndex(
                    pair =>
                        string.Equals(
                            pair.Key,
                            currentDestination,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
        }

        /*
         * A single valid destination can safely be selected by
         * pressing Enter. Multiple destinations require explicit
         * selection unless the profile already has a valid default.
         */
        if (defaultIndex < 0
            && availableDestinations.Count == 1)
        {
            defaultIndex = 0;
        }

        Console.WriteLine();

        while (true)
        {
            Console.Write(
                defaultIndex >= 0
                    ? $"Select destination [{defaultIndex + 1}]: "
                    : "Select destination (number or name): "
            );

            var input =
                Console.ReadLine();

            if (input is null)
            {
                throw new InvalidOperationException(
                    "Input ended before a Zendesk destination " +
                    "was selected."
                );
            }

            input =
                input.Trim();

            if (string.IsNullOrWhiteSpace(input))
            {
                if (defaultIndex >= 0)
                {
                    return availableDestinations[
                        defaultIndex
                    ].Key;
                }

                Console.WriteLine(
                    "Select a destination by number or name."
                );

                continue;
            }

            /*
             * Numbered choices are preferred, matching the existing
             * Zendesk connection selection workflow.
             */
            if (int.TryParse(
                    input,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var selectedIndex
                )
                && selectedIndex >= 1
                && selectedIndex <= availableDestinations.Count)
            {
                return availableDestinations[
                    selectedIndex - 1
                ].Key;
            }

            /*
             * Also support typing an existing destination name,
             * case-insensitively.
             */
            var namedDestination =
                availableDestinations
                    .FirstOrDefault(
                        pair =>
                            string.Equals(
                                pair.Key,
                                input,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

            if (!string.IsNullOrWhiteSpace(
                    namedDestination.Key))
            {
                return namedDestination.Key;
            }

            /*
             * Distinguish an existing but invalid destination
             * from a completely unknown selection.
             */
            var invalidDestination =
                FindZendeskDestination(
                    connection,
                    input
                );

            if (invalidDestination is not null
                && invalidDestination.GroupId <= 0)
            {
                Console.WriteLine(
                    $"Destination '{input}' has an invalid " +
                    "group ID. Update it before selecting."
                );

                continue;
            }

            Console.WriteLine(
                "Choose an available destination by number or name."
            );
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
