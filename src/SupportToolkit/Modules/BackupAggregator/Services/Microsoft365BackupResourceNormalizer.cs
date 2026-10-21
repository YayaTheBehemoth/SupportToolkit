
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
        var teamKeys = resources
            .Where(resource => IsKind(resource, TeamKind))
            .Select(resource => GetLogicalResourceKey(resource, TeamKind))
            .Where(key => key is not null)
            .Select(key => key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Find Groups that should be consolidated into a matching Team.
        // A Team-associated Group with independent SITE protection must
        // instead remain a separate workload.
        var backingGroups = resources
            .Where(resource =>
                IsKind(resource, GroupKind)
                && ShouldConsolidateWithTeam(resource))
            .Select(resource => new
            {
                Resource = resource,
                Key = GetLogicalResourceKey(resource, GroupKind)
            })
            .Where(item =>
                item.Key is not null
                && teamKeys.Contains(item.Key))
            .GroupBy(
                item => item.Key!,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Resource).ToList(),
                StringComparer.OrdinalIgnoreCase);

        var entries = new List<BackupReportEntry>();

        var processedTeamKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            if (IsKind(resource, GroupKind))
            {
                var groupKey =
                    GetLogicalResourceKey(resource, GroupKind);

                // Only skip a Group if a matching Team will account
                // for it. Its health is retained in the Team entry.
                if (groupKey is not null
                    && teamKeys.Contains(groupKey)
                    && ShouldConsolidateWithTeam(resource))
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
                        continue;
                    }

                    if (backingGroups.TryGetValue(
                        teamKey,
                        out var groups))
                    {
                        entries.Add(
                            CreateLogicalTeamEntry(resource, groups));

                        continue;
                    }
                }
            }

            entries.Add(ToReportEntry(resource));
        }

        return entries.AsReadOnly();
    }

    private static bool ShouldConsolidateWithTeam(
        AcronisMicrosoft365ResourceDto group)
    {
        // Preserve the established legacy behavior.
        if (string.IsNullOrWhiteSpace(group.TeamName))
        {
            return true;
        }

        // Observed Tenant A configuration:
        // - Associated Group
        // - Overall protection confirmed
        // - MAILBOX protection true
        // - SITE protection null
        //
        // This Group contributes mailbox protection but not an
        // independently accounted SharePoint workload.
        //
        // Do not generalize null to mean unprotected elsewhere.
        // Missing or contradictory metadata remains separate.
        if (group.HasProtections != true
            || group.BasicKinds is not { Count: 2 })
        {
            return false;
        }

        var protectedMailbox = group.BasicKinds.Count(
            component =>
                component is not null
                && IsBasicKind(component, "MAILBOX")
                && component.HasProtections == true);

        var unconfirmedSite = group.BasicKinds.Count(
            component =>
                component is not null
                && IsBasicKind(component, "SITE")
                && component.HasProtections is null);

        return protectedMailbox == 1 && unconfirmedSite == 1;
    }

    private static bool IsBasicKind(
        AcronisMicrosoft365BasicKindDto component,
        string kind)
    {
        return string.Equals(
            component.Kind,
            kind,
            StringComparison.OrdinalIgnoreCase);
    }

    private static BackupReportEntry CreateLogicalTeamEntry(
        AcronisMicrosoft365ResourceDto team,
        IReadOnlyList<AcronisMicrosoft365ResourceDto> backingGroups)
    {
        // A failed or unknown Group must never disappear merely
        // because its matching Team has a successful backup.
        var candidates = new[] { team }
            .Concat(backingGroups)
            .Select(ToReportEntry)
            .OrderByDescending(
                entry => GetClassificationPriority(
                    entry.Classification))
            .ThenByDescending(
                entry => entry.LastBackupRun
                    ?? DateTimeOffset.MinValue)
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
                "Unsupported backup report classification.")
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

        var key = resource.InternalId[prefix.Length..].Trim();

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
            StringComparison.OrdinalIgnoreCase);
    }

    private static BackupReportEntry ToReportEntry(
        AcronisMicrosoft365ResourceDto resource)
    {
        var protectionState = GetProtectionState(resource);
        var classification = Classify(resource, protectionState);

        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(resource.Name)
                    ? "<unknown Microsoft 365 resource>"
                    : resource.Name,

            LastResult = GetDisplayedResult(
                resource,
                classification,
                protectionState),

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
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                resource.LastTaskState,
                "idle",
                StringComparison.OrdinalIgnoreCase)
            && resource.LastSuccessTime is not null)
        {
            return BackupReportEntryClassification.Healthy;
        }

        return BackupReportEntryClassification.NeedsReview;
    }

    private static ProtectionState GetProtectionState(
        AcronisMicrosoft365ResourceDto resource)
    {
        var protectionSignals = new List<bool?>
        {
            resource.HasProtections
        };

        // Unknown component flags do not automatically mean
        // the resource is unprotected.
        if (resource.BasicKinds is not null)
        {
            protectionSignals.AddRange(
                resource.BasicKinds
                    .Where(component => component is not null)
                    .Select(component => component!.HasProtections));
        }

        var knownProtectionSignals = protectionSignals
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
                    StringComparison.OrdinalIgnoreCase))
            {
                return resource.LastTaskStatus;
            }

            if (!string.IsNullOrWhiteSpace(resource.LastTaskState)
                && !string.Equals(
                    resource.LastTaskState,
                    "idle",
                    StringComparison.OrdinalIgnoreCase))
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
