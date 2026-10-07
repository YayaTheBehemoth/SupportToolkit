using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupActivityNormalizer
{
    public IReadOnlyList<LatestBackupActivity> NormalizeLatestPerResource(
        IReadOnlyList<AcronisActivityDto> activities)
    {
        var normalized =
            activities
                .GroupBy(
                    GetResourceGroupingKey,
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(
                    group =>
                        NormalizeLatest(
                            group.Key,
                            group
                        )
                )
                .OrderBy(
                    activity =>
                        activity.ResourceName,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        return normalized.AsReadOnly();
    }

    private static LatestBackupActivity NormalizeLatest(
        string resourceKey,
        IEnumerable<AcronisActivityDto> activities)
    {
        /*
         * "Latest activity" is based primarily on startedAt.
         *
         * This matters for non-completed activities:
         * an in-progress activity may have no completedAt at all.
         */
        var latest =
            activities
                .OrderByDescending(
                    GetActivityTimestamp
                )
                .First();

        var resourceName =
            latest.Context.ResourceName
            ?? latest.Resource?.Name
            ?? "<unknown resource>";

        return new LatestBackupActivity
        {
            ResourceKey =
                resourceKey,

            ResourceName =
                resourceName,

            State =
                latest.State
                ?? string.Empty,

            ResultCode =
                latest.Result.Code,

            PolicyName =
                latest.Context.PolicyName
                ?? latest.Policy?.Name,

            ResourceKind =
                latest.Context.ResourceKind,

            ResourceSubtype =
                latest.Context.ResourceSubtype,

            ActivityTimestamp =
                GetActivityTimestamp(
                    latest
                ),

            ActivityId =
                latest.Uuid
                ?? latest.Id,

            TaskId =
                latest.TaskIdString
        };
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

        /*
         * Never merge structurally unidentified rows together.
         * Each unexplained activity remains independently visible.
         */
        return
            $"activity:{activity.Uuid
                ?? activity.Id
                ?? Guid.NewGuid().ToString()}";
    }

    private static DateTimeOffset GetActivityTimestamp(
        AcronisActivityDto activity)
    {
        return activity.StartedAt
            ?? activity.CreatedAt
            ?? activity.UpdatedAt
            ?? activity.CompletedAt
            ?? DateTimeOffset.MinValue;
    }
}