using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class DeviceBackupResourceNormalizer
{
    public IReadOnlyList<BackupReportEntry> Normalize(
        IReadOnlyList<AcronisDeviceResourceDto> resources)
    {
        return Deduplicate(resources)
            .Select(ToReportEntry)
            .ToList()
            .AsReadOnly();
    }

    private static BackupReportEntry ToReportEntry(
        AcronisDeviceResourceDto resource)
    {
        var status =
            resource.Status;

        return new BackupReportEntry
        {
            DeviceName =
                resource.Name
                ?? resource.DisplayName
                ?? "<unknown device resource>",

            LastResult =
                status?.State
                ?? "<missing>",

            Classification =
                Classify(resource),

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

    private static BackupReportEntryClassification Classify(
        AcronisDeviceResourceDto resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Name)
            && string.IsNullOrWhiteSpace(resource.DisplayName))
        {
            return BackupReportEntryClassification.Unknown;
        }

        var status =
            resource.Status;

        if (status is null
            || string.IsNullOrWhiteSpace(status.State))
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (IsNotProtectedState(status.State)
            || IsProblemState(status.State))
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
             * Acronis renders an idle machine with no previous backup and a
             * future nextBackup as "backup scheduled", not green OK.
             */
            if (status.LastBackup is null
                && status.NextBackup is not null)
            {
                return BackupReportEntryClassification.NeedsReview;
            }

            return status.LastSuccessBackup is not null
                ? BackupReportEntryClassification.Healthy
                : BackupReportEntryClassification.Unknown;
        }

        /*
         * Unknown vendor states stay visible. We never silently suppress a
         * state that SupportToolkit does not understand.
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
            "critical" => true,
            "error" => true,
            "warning" => true,
            "interaction" => true,
            "need_interaction" => true,
            "paused" => true,
            "canceled" => true,
            "cancelled" => true,
            "running" => true,
            _ => false
        };
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        Deduplicate(
            IReadOnlyList<AcronisDeviceResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisDeviceResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisDeviceResourceDto>();

        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                withoutIdentity.Add(resource);
                continue;
            }

            unique[resource.Id] = resource;
        }

        return unique.Values
            .Concat(withoutIdentity)
            .ToList()
            .AsReadOnly();
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(
            value =>
                !string.IsNullOrWhiteSpace(value)
        );
    }
}
