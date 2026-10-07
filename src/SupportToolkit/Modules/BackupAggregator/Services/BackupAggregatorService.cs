using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Activities.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupAggregatorService
{
    private readonly BackupActivityNormalizer _normalizer;
    private readonly BackupActivityClassifier _classifier;

    public BackupAggregatorService(
        BackupActivityNormalizer normalizer,
        BackupActivityClassifier classifier)
    {
        _normalizer =
            normalizer;

        _classifier =
            classifier;
    }

    public TenantBackupReport BuildTenantReport(
        string tenantName,
        IReadOnlyList<AcronisActivityDto> activities)
    {
        var latestActivities =
            _normalizer.NormalizeLatestPerResource(
                activities
            );

        var entries =
            latestActivities
                .Select(
                    ToReportEntry
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

    public AggregatedBackupReport BuildAggregatedReport(
        IReadOnlyList<TenantBackupReport> tenantReports)
    {
        return new AggregatedBackupReport
        {
            Tenants =
                tenantReports
        };
    }

    private BackupReportEntry ToReportEntry(
        LatestBackupActivity activity)
    {
        var classification =
            _classifier.Classify(
                activity
            );

        return new BackupReportEntry
        {
            DeviceName =
                activity.ResourceName,

            LastResult =
                activity.ResultCode
                ?? "<missing>",

            Classification =
                classification,

            LastBackupRun =
                activity.ActivityTimestamp
                    == DateTimeOffset.MinValue
                    ? null
                    : activity.ActivityTimestamp,

            LastSuccessfulBackup =
                null,

            PlanName =
                activity.PolicyName,

            DeviceState =
                string.IsNullOrWhiteSpace(
                    activity.State)
                    ? null
                    : activity.State
        };
    }
}