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
        return new BackupReportEntry
        {
            ResourceName =
                string.IsNullOrWhiteSpace(
                    resource.Name
                )
                    ? "<unknown Microsoft 365 resource>"
                    : resource.Name,

            LastResult =
                resource.LastTaskStatus
                ?? "<missing>",

            Classification =
                Classify(
                    resource
                ),

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
        AcronisMicrosoft365ResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(
                resource.Name
            ))
        {
            return BackupReportEntryClassification.Unknown;
        }

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
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Contradictory Acronis protection signals must never be suppressed.
         *
         * They indicate that SupportToolkit cannot safely infer one canonical
         * protection state from the payload.
         */
        if (knownProtectionSignals.Count > 1)
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        if (!knownProtectionSignals[0])
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
}