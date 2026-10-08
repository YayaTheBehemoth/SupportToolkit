using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Epm.Dtos;
using SupportToolkit.Providers.Acronis.O365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Normalizes current-state Acronis inventory resources into the
/// BackupAggregator reporting model.
///
/// Classification is intentionally conservative:
/// only states supported by observed Acronis semantics are suppressed.
/// </summary>
public sealed class BackupInventoryNormalizer
{
    public TenantBackupReport BuildTenantReport(
        string tenantName,
        IReadOnlyList<AcronisO365ResourceDto> o365Resources,
        IReadOnlyList<AcronisEpmResourceDto> epmResources)
    {
        var entries =
            NormalizeO365(
                o365Resources
            )
            .Concat(
                NormalizeEpm(
                    epmResources
                )
            )
            .ToList()
            .AsReadOnly();

        return new TenantBackupReport
        {
            TenantName =
                tenantName,

            Entries =
                entries
        };
    }

    private static IEnumerable<BackupReportEntry>
        NormalizeO365(
            IReadOnlyList<AcronisO365ResourceDto> resources)
    {
        foreach (var resource in DeduplicateO365(
                     resources))
        {
            var name =
                string.IsNullOrWhiteSpace(
                    resource.Name)
                    ? "<unknown O365 resource>"
                    : resource.Name;

            yield return new BackupReportEntry
            {
                DeviceName =
                    name,

                LastResult =
                    resource.LastTaskStatus
                    ?? "<missing>",

                Classification =
                    ClassifyO365(
                        resource
                    ),

                LastBackupRun =
                    resource.LastFinishTime
                    ?? resource.LastStartTime,

                LastSuccessfulBackup =
                    resource.LastSuccessTime,

                PlanName =
                    null,

                DeviceState =
                    resource.LastTaskState
            };
        }
    }

    private static IEnumerable<BackupReportEntry>
        NormalizeEpm(
            IReadOnlyList<AcronisEpmResourceDto> resources)
    {
        foreach (var resource in DeduplicateEpm(
                     resources))
        {
            var status =
                resource.Status;

            var name =
                resource.Name
                ?? resource.DisplayName
                ?? "<unknown EPM resource>";

            yield return new BackupReportEntry
            {
                DeviceName =
                    name,

                LastResult =
                    status?.State
                    ?? "<missing>",

                Classification =
                    ClassifyEpm(
                        resource
                    ),

                LastBackupRun =
                    status?.LastBackup
                    ?? status?.LastActionTime,

                LastSuccessfulBackup =
                    status?.LastSuccessBackup,

                PlanName =
                    FirstNonEmpty(
                        status?.NextBackupPolicy,
                        status?.AppliedPolicyNames
                    ),

                DeviceState =
                    status?.State
            };
        }
    }

    private static BackupReportEntryClassification
        ClassifyO365(
            AcronisO365ResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(
                resource.Name))
        {
            return BackupReportEntryClassification.Unknown;
        }

        var protectionSignals =
            new List<bool?>
            {
                resource.HasProtections
            };

        protectionSignals.AddRange(
            resource.BasicKinds.Select(
                kind =>
                    kind.HasProtections
            )
        );

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
                .ToList();

        if (knownProtectionSignals.Count == 0)
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (!knownProtectionSignals.Any(
                value =>
                    value))
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (string.IsNullOrWhiteSpace(
                resource.LastTaskStatus)
            || string.IsNullOrWhiteSpace(
                resource.LastTaskState))
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

    private static BackupReportEntryClassification
        ClassifyEpm(
            AcronisEpmResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(
                resource.Name)
            && string.IsNullOrWhiteSpace(
                resource.DisplayName))
        {
            return BackupReportEntryClassification.Unknown;
        }

        var status =
            resource.Status;

        if (status is null
            || string.IsNullOrWhiteSpace(
                status.State))
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Confirmed from the Acronis web client status renderer:
         *
         *   "ok" / "idle"
         *       -> resources-status-ok
         *       -> green success icon
         *       -> MACHINELIST_STATUS_OK
         *
         *   "not_protected" / "notProtected"
         *       -> resources-status-not-protected
         *       -> MACHINESIMPLE_NOT_PROTECTED_TEXT
         *
         * The web client also changes an idle machine with no previous
         * backup but a future nextBackup into "backup_scheduled".
         *
         * We deliberately require a successful backup before suppressing
         * an idle resource. This is stricter than merely reproducing the
         * presentation layer.
         */

        if (IsNotProtectedState(
                status.State))
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (IsProblemState(
                status.State))
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (string.Equals(
                status.State,
                "idle",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                status.State,
                "ok",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            /*
             * Acronis renders this special case as "backup scheduled",
             * not green OK.
             *
             * Keep it visible until BackupAggregator has an explicit
             * operational policy for newly scheduled resources.
             */
            if (status.LastBackup is null
                && status.NextBackup is not null)
            {
                return BackupReportEntryClassification.NeedsReview;
            }

            if (status.LastSuccessBackup
                is not null)
            {
                return BackupReportEntryClassification.Healthy;
            }

            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Do not silently suppress an Acronis state that we have not
         * explicitly classified.
         */
        return BackupReportEntryClassification.NeedsReview;
    }

    private static bool IsNotProtectedState(
        string state)
    {
        return string.Equals(
                   state,
                   "notProtected",
                   StringComparison.OrdinalIgnoreCase
               )
               || string.Equals(
                   state,
                   "not_protected",
                   StringComparison.OrdinalIgnoreCase
               )
               || string.Equals(
                   state,
                   "not_run",
                   StringComparison.OrdinalIgnoreCase
               );
    }

    private static bool IsProblemState(
        string state)
    {
        return state.ToLowerInvariant() switch
        {
            "critical" =>
                true,

            "error" =>
                true,

            "warning" =>
                true,

            "interaction" =>
                true,

            "need_interaction" =>
                true,

            "paused" =>
                true,

            "canceled" =>
                true,

            "cancelled" =>
                true,

            "running" =>
                true,

            _ =>
                false
        };
    }

    private static IReadOnlyList<AcronisO365ResourceDto>
        DeduplicateO365(
            IReadOnlyList<AcronisO365ResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisO365ResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisO365ResourceDto>();

        foreach (var resource in resources)
        {
            var identity =
                resource.Id
                ?? resource.InternalId;

            if (string.IsNullOrWhiteSpace(
                    identity))
            {
                withoutIdentity.Add(
                    resource
                );

                continue;
            }

            unique[identity] =
                resource;
        }

        return unique
            .Values
            .Concat(
                withoutIdentity
            )
            .ToList()
            .AsReadOnly();
    }

    private static IReadOnlyList<AcronisEpmResourceDto>
        DeduplicateEpm(
            IReadOnlyList<AcronisEpmResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisEpmResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisEpmResourceDto>();

        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(
                    resource.Id))
            {
                withoutIdentity.Add(
                    resource
                );

                continue;
            }

            unique[resource.Id] =
                resource;
        }

        return unique
            .Values
            .Concat(
                withoutIdentity
            )
            .ToList()
            .AsReadOnly();
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(
                    value
                )
        );
    }
}