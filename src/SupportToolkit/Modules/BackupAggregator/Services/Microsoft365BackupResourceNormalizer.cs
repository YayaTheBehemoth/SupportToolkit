using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class Microsoft365BackupResourceNormalizer
{
    public IReadOnlyList<BackupReportEntry> Normalize(
        IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
    {
        return Deduplicate(resources)
            .Select(ToReportEntry)
            .ToList()
            .AsReadOnly();
    }

    private static BackupReportEntry ToReportEntry(
        AcronisMicrosoft365ResourceDto resource)
    {
        return new BackupReportEntry
        {
            DeviceName =
                string.IsNullOrWhiteSpace(resource.Name)
                    ? "<unknown Microsoft 365 resource>"
                    : resource.Name,

            LastResult =
                resource.LastTaskStatus
                ?? "<missing>",

            Classification =
                Classify(resource),

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

    private static BackupReportEntryClassification Classify(
        AcronisMicrosoft365ResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
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
                basicKind =>
                    basicKind.HasProtections
            )
        );

        var knownProtectionSignals =
            protectionSignals
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToList();

        if (knownProtectionSignals.Count == 0)
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (!knownProtectionSignals.Any(value => value))
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

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        Deduplicate(
            IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisMicrosoft365ResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisMicrosoft365ResourceDto>();

        foreach (var resource in resources)
        {
            var identity =
                string.IsNullOrWhiteSpace(resource.Id)
                    ? resource.InternalId
                    : resource.Id;

            if (string.IsNullOrWhiteSpace(identity))
            {
                withoutIdentity.Add(resource);
                continue;
            }

            unique[identity] = resource;
        }

        return unique.Values
            .Concat(withoutIdentity)
            .ToList()
            .AsReadOnly();
    }
}
