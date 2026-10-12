using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class Microsoft365BackupResourceNormalizer
{
    private const string TeamKind =
        "TEAM";

    private const string GroupKind =
        "GROUP";

    public IReadOnlyList<BackupReportEntry> Normalize(
        IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
    {
        var teamKeys =
            resources
                .Where(
                    resource =>
                        IsKind(
                            resource,
                            TeamKind
                        )
                )
                .Select(
                    resource =>
                        GetLogicalResourceKey(
                            resource,
                            TeamKind
                        )
                )
                .Where(
                    key =>
                        key is not null
                )
                .Select(
                    key =>
                        key!
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        var backingGroups =
            resources
                .Where(
                    resource =>
                        IsKind(
                            resource,
                            GroupKind
                        )
                )
                .Select(
                    resource =>
                        new
                        {
                            Resource =
                                resource,

                            Key =
                                GetLogicalResourceKey(
                                    resource,
                                    GroupKind
                                )
                        }
                )
                .Where(
                    item =>
                        item.Key is not null
                        && teamKeys.Contains(
                            item.Key
                        )
                )
                .GroupBy(
                    item =>
                        item.Key!,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        group
                            .Select(
                                item =>
                                    item.Resource
                            )
                            .ToList(),

                    StringComparer.OrdinalIgnoreCase
                );

        var entries =
            new List<BackupReportEntry>();

        var processedTeamKeys =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var resource in resources)
        {
            if (IsKind(
                    resource,
                    GroupKind
                ))
            {
                var groupKey =
                    GetLogicalResourceKey(
                        resource,
                        GroupKind
                    );

                /*
                 * Microsoft 365 Teams are represented by Acronis as both a
                 * TEAM resource and the Microsoft 365 GROUP that backs it.
                 *
                 * When both resources share the same Acronis logical identity,
                 * the GROUP belongs to the Teams workload and must not become a
                 * second user-facing workload.
                 *
                 * Its health state is not discarded. It is merged into the
                 * corresponding TEAM entry when that TEAM is processed.
                 */
                if (groupKey is not null
                    && teamKeys.Contains(
                        groupKey
                    ))
                {
                    continue;
                }
            }

            if (IsKind(
                    resource,
                    TeamKind
                ))
            {
                var teamKey =
                    GetLogicalResourceKey(
                        resource,
                        TeamKind
                    );

                if (teamKey is not null)
                {
                    if (!processedTeamKeys.Add(
                            teamKey
                        ))
                    {
                        /*
                         * Multiple API representations of the same logical
                         * Teams identity must produce only one report row.
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

            entries.Add(
                ToReportEntry(
                    resource
                )
            );
        }

        return entries
            .AsReadOnly();
    }

    private static BackupReportEntry CreateLogicalTeamEntry(
        AcronisMicrosoft365ResourceDto team,
        IReadOnlyList<AcronisMicrosoft365ResourceDto> backingGroups)
    {
        var candidates =
            new[]
                {
                    team
                }
                .Concat(
                    backingGroups
                )
                .Select(
                    ToReportEntry
                )
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

        var selected =
            candidates[0];

        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(
                    team.Name
                )
                    ? selected.ResourceName
                    : team.Name,

            LastResult =
                selected.LastResult,

            Classification =
                selected.Classification,

            LastBackupRun =
                selected.LastBackupRun,

            LastSuccessfulBackup =
                selected.LastSuccessfulBackup,

            PlanName =
                selected.PlanName,

            ResourceState =
                selected.ResourceState
        };
    }

    private static int GetClassificationPriority(
        BackupReportEntryClassification classification)
    {
        return classification switch
        {
            BackupReportEntryClassification.NeedsReview =>
                2,

            BackupReportEntryClassification.Unknown =>
                1,

            BackupReportEntryClassification.Healthy =>
                0,

            _ =>
                throw new ArgumentOutOfRangeException(
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
        if (string.IsNullOrWhiteSpace(
                resource.InternalId))
        {
            return null;
        }

        var prefix =
            expectedKind
            + ".";

        if (!resource.InternalId.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var key =
            resource.InternalId[
                prefix.Length..
            ]
            .Trim();

        return string.IsNullOrWhiteSpace(
                key)
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
            GetProtectionState(
                resource
            );

        var classification =
            Classify(
                resource,
                protectionState
            );

        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(
                    resource.Name
                )
                    ? "<unknown Microsoft 365 resource>"
                    : resource.Name,

            LastResult =
                GetDisplayedResult(
                    resource,
                    classification,
                    protectionState
                ),

            Classification =
                classification,

            LastBackupRun =
                resource.LastFinishTime
                ?? resource.LastStartTime,

            LastSuccessfulBackup =
                resource.LastSuccessTime,

            PlanName =
                null,

            ResourceState =
                resource.LastTaskState
        };
    }

    private static BackupReportEntryClassification Classify(
        AcronisMicrosoft365ResourceDto resource,
        ProtectionState protectionState)
    {
        if (string.IsNullOrWhiteSpace(
                resource.Name
            ))
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (protectionState
            == ProtectionState.Unknown)
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Contradictory Acronis protection signals must never be suppressed.
         *
         * They indicate that SupportToolkit cannot safely infer one canonical
         * protection state from the payload.
         */
        if (protectionState
            == ProtectionState.Conflicting)
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (protectionState
            == ProtectionState.Unprotected)
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (string.IsNullOrWhiteSpace(
                resource.LastTaskStatus
            )
            || string.IsNullOrWhiteSpace(
                resource.LastTaskState
            ))
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
         * Acronis can explicitly return null for basicKinds when the
         * Microsoft 365 payload does not contain per-kind protection data.
         *
         * Individual null elements are also ignored because they contain no
         * usable protection signal.
         */
        if (resource.BasicKinds is not null)
        {
            protectionSignals.AddRange(
                resource.BasicKinds
                    .Where(
                        basicKind =>
                            basicKind is not null
                    )
                    .Select(
                        basicKind =>
                            basicKind!.HasProtections
                    )
            );
        }

        var knownProtectionSignals =
            protectionSignals
                .Where(
                    value =>
                        value.HasValue
                )
                .Select(
                    value =>
                        value!.Value
                )
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
        /*
         * For Microsoft 365 resources, the task result alone does not always
         * explain why SupportToolkit surfaced the resource.
         *
         * For example, Acronis can report the most recent task as "ok" while
         * the resource itself has conflicting or absent protection.
         *
         * Prefer the actual review reason in those cases so the exception
         * report never describes a finding as healthy.
         */
        if (protectionState
            == ProtectionState.Conflicting)
        {
            return "protection_conflict";
        }

        if (protectionState
            == ProtectionState.Unprotected)
        {
            return "not_protected";
        }

        if (classification
            == BackupReportEntryClassification.NeedsReview)
        {
            /*
             * Preserve an explicit vendor task failure before falling back to
             * derived SupportToolkit review reasons.
             */
            if (!string.IsNullOrWhiteSpace(
                    resource.LastTaskStatus
                )
                && !string.Equals(
                    resource.LastTaskStatus,
                    "ok",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                return resource.LastTaskStatus;
            }

            /*
             * Likewise, retain a non-idle task state when the status itself
             * does not explain why the resource is visible.
             */
            if (!string.IsNullOrWhiteSpace(
                    resource.LastTaskState
                )
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