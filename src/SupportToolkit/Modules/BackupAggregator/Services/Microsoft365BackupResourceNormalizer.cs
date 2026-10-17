
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class Microsoft365BackupResourceNormalizer
{
    private const string TeamKind = "TEAM";
    private const string GroupKind = "GROUP";

    public IReadOnlyList<BackupReportEntry> Normalize(
        IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
    {
        var teamKeys =
            resources
                .Where(resource => IsKind(resource, TeamKind))
                .Select(
                    resource =>
                        GetLogicalResourceKey(resource, TeamKind)
                )
                .Where(key => key is not null)
                .Select(key => key!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        /*
         * Only Groups without an explicit Team association remain eligible
         * for legacy consolidation.
         *
         * Production validation confirmed that Acronis exposes a populated
         * teamName on Groups that it counts as separate protected workloads.
         *
         * Those Groups must not contribute to a merged Team entry.
         */
        var backingGroups =
            resources
                .Where(
                    resource =>
                        IsKind(resource, GroupKind)
                        && string.IsNullOrWhiteSpace(resource.TeamName)
                )
                .Select(
                    resource =>
                        new
                        {
                            Resource = resource,
                            Key = GetLogicalResourceKey(
                                resource,
                                GroupKind
                            )
                        }
                )
                .Where(
                    item =>
                        item.Key is not null
                        && teamKeys.Contains(item.Key)
                )
                .GroupBy(
                    item => item.Key!,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group.Select(item => item.Resource).ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

        var entries = new List<BackupReportEntry>();

        var processedTeamKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var resource in resources)
        {
            if (IsKind(resource, GroupKind))
            {
                var groupKey =
                    GetLogicalResourceKey(resource, GroupKind);

                /*
                 * Preserve the existing consolidation of a backing Group
                 * when Acronis supplies no explicit Team association.
                 *
                 * A Group with a populated teamName is retained as its own
                 * workload, even when its internal identity matches a Team.
                 *
                 * The Group's protection flags do not determine whether
                 * it is consolidated.
                 */
                if (groupKey is not null
                    && teamKeys.Contains(groupKey)
                    && string.IsNullOrWhiteSpace(resource.TeamName))
                {
                    continue;
                }
            }

            if (IsKind(resource, TeamKind))
            {
                var teamKey =
                    GetLogicalResourceKey(resource, TeamKind);

                if (teamKey is not null)
                {
                    if (!processedTeamKeys.Add(teamKey))
                    {
                        /*
                         * Preserve the existing handling of repeated
                         * representations of the same Team identity.
                         */
                        continue;
                    }

                    if (backingGroups.TryGetValue(
                            teamKey,
                            out var groups))
                    {
                        entries.Add(
                            CreateLogicalTeamEntry(
                                resource,
                                groups
                            )
                        );

                        continue;
                    }
                }
            }

            entries.Add(ToReportEntry(resource));
        }

        return entries.AsReadOnly();
    }

    private static BackupReportEntry CreateLogicalTeamEntry(
        AcronisMicrosoft365ResourceDto team,
        IReadOnlyList<AcronisMicrosoft365ResourceDto> backingGroups)
    {
        var candidates =
            new[] { team }
                .Concat(backingGroups)
                .Select(ToReportEntry)
                .OrderByDescending(
                    entry =>
                        GetClassificationPriority(
                            entry.Classification
                        )
                )
                .ThenByDescending(
                    entry =>
                        entry.LastBackupRun
                        ?? DateTimeOffset.MinValue
                )
                .ToList();

        var selected = candidates[0];

        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(team.Name)
                    ? selected.ResourceName
                    : team.Name,

            LastResult = selected.LastResult,
            Classification = selected.Classification,
            LastBackupRun = selected.LastBackupRun,
            LastSuccessfulBackup = selected.LastSuccessfulBackup,
            PlanName = selected.PlanName,
            ResourceState = selected.ResourceState
        };
    }

    private static int GetClassificationPriority(
        BackupReportEntryClassification classification)
    {
        return classification switch
        {
            BackupReportEntryClassification.NeedsReview => 2,
            BackupReportEntryClassification.Unknown => 1,
            BackupReportEntryClassification.Healthy => 0,

            _ => throw new ArgumentOutOfRangeException(
                nameof(classification),
                classification,
                "Unsupported backup report classification."
            )
        };
    }

    private static string? GetLogicalResourceKey(
        AcronisMicrosoft365ResourceDto resource,
        string expectedKind)
    {
        if (string.IsNullOrWhiteSpace(resource.InternalId))
        {
            return null;
        }

        var prefix = expectedKind + ".";

        if (!resource.InternalId.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key =
            resource.InternalId[prefix.Length..].Trim();

        return string.IsNullOrWhiteSpace(key)
            ? null
            : key;
    }

    private static bool IsKind(
        AcronisMicrosoft365ResourceDto resource,
        string kind)
    {
        return string.Equals(
            resource.Kind,
            kind,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static BackupReportEntry ToReportEntry(
        AcronisMicrosoft365ResourceDto resource)
    {
        var protectionState =
            GetProtectionState(resource);

        var classification =
            Classify(resource, protectionState);

        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(resource.Name)
                    ? "<unknown Microsoft 365 resource>"
                    : resource.Name,

            LastResult =
                GetDisplayedResult(
                    resource,
                    classification,
                    protectionState
                ),

            Classification = classification,

            LastBackupRun =
                resource.LastFinishTime
                ?? resource.LastStartTime,

            LastSuccessfulBackup = resource.LastSuccessTime,
            PlanName = null,
            ResourceState = resource.LastTaskState
        };
    }

    private static BackupReportEntryClassification Classify(
        AcronisMicrosoft365ResourceDto resource,
        ProtectionState protectionState)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (protectionState == ProtectionState.Unknown)
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Contradictory Acronis protection signals must never be suppressed.
         */
        if (protectionState == ProtectionState.Conflicting)
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (protectionState == ProtectionState.Unprotected)
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (string.IsNullOrWhiteSpace(resource.LastTaskStatus)
            || string.IsNullOrWhiteSpace(resource.LastTaskState))
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (string.Equals(
                resource.LastTaskStatus,
                "ok",
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                resource.LastTaskState,
                "idle",
                StringComparison.OrdinalIgnoreCase
            )
            && resource.LastSuccessTime is not null)
        {
            return BackupReportEntryClassification.Healthy;
        }

        return BackupReportEntryClassification.NeedsReview;
    }

    private static ProtectionState GetProtectionState(
        AcronisMicrosoft365ResourceDto resource)
    {
        var protectionSignals =
            new List<bool?>
            {
                resource.HasProtections
            };

        /*
         * Acronis may return null for basicKinds or its protection flags.
         * Unknown signals do not automatically imply unprotected resources.
         */
        if (resource.BasicKinds is not null)
        {
            protectionSignals.AddRange(
                resource.BasicKinds
                    .Where(basicKind => basicKind is not null)
                    .Select(
                        basicKind =>
                            basicKind!.HasProtections
                    )
            );
        }

        var knownProtectionSignals =
            protectionSignals
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Distinct()
                .ToList();

        if (knownProtectionSignals.Count == 0)
        {
            return ProtectionState.Unknown;
        }

        if (knownProtectionSignals.Count > 1)
        {
            return ProtectionState.Conflicting;
        }

        return knownProtectionSignals[0]
            ? ProtectionState.Protected
            : ProtectionState.Unprotected;
    }

    private static string GetDisplayedResult(
        AcronisMicrosoft365ResourceDto resource,
        BackupReportEntryClassification classification,
        ProtectionState protectionState)
    {
        if (protectionState == ProtectionState.Conflicting)
        {
            return "protection_conflict";
        }

        if (protectionState == ProtectionState.Unprotected)
        {
            return "not_protected";
        }

        if (classification
            == BackupReportEntryClassification.NeedsReview)
        {
            if (!string.IsNullOrWhiteSpace(resource.LastTaskStatus)
                && !string.Equals(
                    resource.LastTaskStatus,
                    "ok",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return resource.LastTaskStatus;
            }

            if (!string.IsNullOrWhiteSpace(resource.LastTaskState)
                && !string.Equals(
                    resource.LastTaskState,
                    "idle",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return resource.LastTaskState;
            }

            if (resource.LastSuccessTime is null)
            {
                return "no_successful_backup";
            }
        }

        return resource.LastTaskStatus
            ?? resource.LastTaskState
            ?? "<missing>";
    }

    private enum ProtectionState
    {
        Unknown,
        Protected,
        Unprotected,
        Conflicting
    }
}
