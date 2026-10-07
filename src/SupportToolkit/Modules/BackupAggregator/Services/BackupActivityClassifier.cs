using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupActivityClassifier
{
    public BackupReportEntryClassification Classify(
        LatestBackupActivity activity)
    {
        if (string.IsNullOrWhiteSpace(
                activity.ResourceName)
            || string.Equals(
                activity.ResourceName,
                "<unknown resource>",
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(
                activity.State)
            || activity.ActivityTimestamp
                == DateTimeOffset.MinValue)
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * First implementation is deliberately conservative.
         *
         * The operator currently wants one simple rule:
         *
         * completed
         *     -> checked and suppressed
         *
         * literally anything else
         *     -> surfaced for review
         *
         * Result-code semantics can be refined after real-world
         * non-completed activities have been observed.
         */
        if (string.Equals(
                activity.State,
                "completed",
                StringComparison.OrdinalIgnoreCase))
        {
            return BackupReportEntryClassification.Healthy;
        }

        return BackupReportEntryClassification.NeedsReview;
    }
}