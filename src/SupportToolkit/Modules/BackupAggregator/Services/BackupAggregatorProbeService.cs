using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Temporary diagnostic surface for verifying Acronis Task Manager
/// activity identity, resource grouping, and latest-activity semantics.
/// </summary>
public sealed class BackupAggregatorProbeService
{
    private const int MaxDisplayedActivities =
        20;

    public int ProbeActivities(
        IReadOnlyList<AcronisActivityDto> activities)
    {
        Console.WriteLine();

        Console.WriteLine(
            "Backup Aggregator Activity Probe"
        );

        Console.WriteLine(
            "================================"
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Activities received: {activities.Count}"
        );

        Console.WriteLine(
            $"Activities displayed: " +
            $"{Math.Min(activities.Count, MaxDisplayedActivities)}"
        );

        Console.WriteLine();

        if (activities.Count == 0)
        {
            Console.WriteLine(
                "No activities were returned."
            );

            return 1;
        }

        foreach (var activity
                 in activities.Take(
                     MaxDisplayedActivities
                 ))
        {
            WriteActivity(
                activity
            );
        }

        WriteObservedValues(
            activities
        );

        WriteGroupingPreview(
            activities
        );

        return 0;
    }

    private static void WriteActivity(
        AcronisActivityDto activity)
    {
        Console.WriteLine(
            new string(
                '-',
                80
            )
        );

        Console.WriteLine(
            $"Activity UUID:      " +
            $"{Display(activity.Uuid)}"
        );

        Console.WriteLine(
            $"Activity ID:        " +
            $"{Display(activity.Id)}"
        );

        Console.WriteLine(
            $"Task ID:            " +
            $"{Display(activity.TaskIdString)}"
        );

        Console.WriteLine(
            $"State:              " +
            $"{Display(activity.State)}"
        );

        Console.WriteLine(
            $"Result code:        " +
            $"{Display(activity.Result.Code)}"
        );

        Console.WriteLine(
            $"Activity type:      " +
            $"{Display(activity.Context.ActivityType)}"
        );

        Console.WriteLine(
            $"Policy ID:          " +
            $"{Display(
                activity.Context.PolicyId
                ?? activity.Policy?.Id
            )}"
        );

        Console.WriteLine(
            $"Policy name:        " +
            $"{Display(
                activity.Context.PolicyName
                ?? activity.Policy?.Name
            )}"
        );

        Console.WriteLine(
            $"Policy type:        " +
            $"{Display(activity.Policy?.Type)}"
        );

        Console.WriteLine(
            $"Resource ID:        " +
            $"{Display(
                activity.Context.ResourceId
                ?? activity.Resource?.Id
            )}"
        );

        Console.WriteLine(
            $"Resource name:      " +
            $"{Display(
                activity.Context.ResourceName
                ?? activity.Resource?.Name
            )}"
        );

        Console.WriteLine(
            $"Resource kind:      " +
            $"{Display(activity.Context.ResourceKind)}"
        );

        Console.WriteLine(
            $"Resource subtype:   " +
            $"{Display(activity.Context.ResourceSubtype)}"
        );

        Console.WriteLine(
            $"Resource type:      " +
            $"{Display(activity.Resource?.Type)}"
        );

        Console.WriteLine(
            $"Started:            " +
            $"{Display(activity.StartedAt)}"
        );

        Console.WriteLine(
            $"Completed:          " +
            $"{Display(activity.CompletedAt)}"
        );

        Console.WriteLine(
            $"Started by:         " +
            $"{Display(activity.StartedByUser)}"
        );

        if (activity.StartedAt is not null
            && activity.CompletedAt is not null)
        {
            var duration =
                activity.CompletedAt.Value
                - activity.StartedAt.Value;

            Console.WriteLine(
                $"Duration:           {duration}"
            );
        }

        Console.WriteLine();
    }

    private static void WriteObservedValues(
        IReadOnlyList<AcronisActivityDto> activities)
    {
        Console.WriteLine(
            new string(
                '=',
                80
            )
        );

        Console.WriteLine(
            "Observed API vocabulary"
        );

        Console.WriteLine(
            "======================="
        );

        Console.WriteLine();

        WriteDistinctValues(
            "States",
            activities.Select(
                activity =>
                    activity.State
            )
        );

        WriteDistinctValues(
            "Result codes",
            activities.Select(
                activity =>
                    activity.Result.Code
            )
        );

        WriteDistinctValues(
            "Activity types",
            activities.Select(
                activity =>
                    activity.Context.ActivityType
            )
        );

        WriteDistinctValues(
            "Policy types",
            activities.Select(
                activity =>
                    activity.Policy?.Type
            )
        );

        WriteDistinctValues(
            "Resource kinds",
            activities.Select(
                activity =>
                    activity.Context.ResourceKind
            )
        );

        WriteDistinctValues(
            "Resource subtypes",
            activities.Select(
                activity =>
                    activity.Context.ResourceSubtype
            )
        );

        WriteDistinctValues(
            "Resource types",
            activities.Select(
                activity =>
                    activity.Resource?.Type
            )
        );
    }

    private static void WriteGroupingPreview(
        IReadOnlyList<AcronisActivityDto> activities)
    {
        Console.WriteLine(
            new string(
                '=',
                80
            )
        );

        Console.WriteLine(
            "Latest backup activity per resource"
        );

        Console.WriteLine(
            "==================================="
        );

        Console.WriteLine();

        var backupActivities =
            activities
                .Where(
                    activity =>
                        string.Equals(
                            activity.Context.ActivityType,
                            "backup",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || string.Equals(
                            activity.Policy?.Type,
                            "backup",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        Console.WriteLine(
            $"Backup activities considered: " +
            $"{backupActivities.Count}"
        );

        var groups =
            backupActivities
                .GroupBy(
                    GetResourceGroupingKey,
                    StringComparer.OrdinalIgnoreCase
                )
                .OrderBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        Console.WriteLine(
            $"Resource groups: {groups.Count}"
        );

        Console.WriteLine();

        foreach (var group
                 in groups.Take(20))
        {
            var latest =
                group
                    .OrderByDescending(
                        GetActivityTimestamp
                    )
                    .First();

            Console.WriteLine(
                $"Resource key: " +
                $"{group.Key}"
            );

            Console.WriteLine(
                $"  Activities in group: " +
                $"{group.Count()}"
            );

            Console.WriteLine(
                $"  Latest result: " +
                $"{Display(latest.Result.Code)}"
            );

            Console.WriteLine(
                $"  Latest state: " +
                $"{Display(latest.State)}"
            );

            Console.WriteLine(
                $"  Latest timestamp: " +
                $"{Display(GetActivityTimestamp(latest))}"
            );

            Console.WriteLine(
                $"  Latest task ID: " +
                $"{Display(latest.TaskIdString)}"
            );

            Console.WriteLine();
        }
    }

    private static string GetResourceGroupingKey(
        AcronisActivityDto activity)
    {
        var resourceId =
            activity.Context.ResourceId
            ?? activity.Resource?.Id;

        if (!string.IsNullOrWhiteSpace(
                resourceId))
        {
            return $"id:{resourceId}";
        }

        var resourceName =
            activity.Context.ResourceName
            ?? activity.Resource?.Name;

        if (!string.IsNullOrWhiteSpace(
                resourceName))
        {
            return $"name:{resourceName}";
        }

        return $"activity:{activity.Uuid ?? activity.Id ?? "<unknown>"}";
    }

    private static DateTimeOffset GetActivityTimestamp(
        AcronisActivityDto activity)
    {
        return activity.CompletedAt
            ?? activity.StartedAt
            ?? activity.UpdatedAt
            ?? activity.CreatedAt
            ?? DateTimeOffset.MinValue;
    }

    private static void WriteDistinctValues(
        string heading,
        IEnumerable<string?> values)
    {
        Console.WriteLine(
            $"{heading}:"
        );

        var distinctValues =
            values
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value
                        )
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .OrderBy(
                    value =>
                        value,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        if (distinctValues.Count == 0)
        {
            Console.WriteLine(
                "  <none>"
            );

            Console.WriteLine();

            return;
        }

        foreach (var value
                 in distinctValues)
        {
            Console.WriteLine(
                $"  {value}"
            );
        }

        Console.WriteLine();
    }

    private static string Display(
        string? value)
    {
        return string.IsNullOrWhiteSpace(
                value)
            ? "<missing>"
            : value;
    }

    private static string Display(
        DateTimeOffset? value)
    {
        return value is null
            ? "<missing>"
            : value.Value.ToString(
                "O"
            );
    }

    private static string Display(
        DateTimeOffset value)
    {
        return value == DateTimeOffset.MinValue
            ? "<missing>"
            : value.ToString(
                "O"
            );
    }
}