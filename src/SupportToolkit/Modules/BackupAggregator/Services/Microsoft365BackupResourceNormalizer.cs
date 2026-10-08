using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class Microsoft365BackupResourceNormalizer
{
    public IReadOnlyList<BackupReportEntry> Normalize(
        IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
    {
        return resources
            .Select(
                ToReportEntry
            )
            .ToList()
            .AsReadOnly();
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