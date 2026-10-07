using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupActivityClassifier
{
    public BackupReportEntryClassification Classify(
        LatestBackupActivity activity)
    {
        /*
         * Classification is deliberately conservative.
         *
         * A resource is only considered healthy when SupportToolkit can
         * positively prove that the latest activity:
         *
         *   1. has usable identity/timestamp data
         *   2. completed
         *   3. completed successfully
         *
         * Anything else remains visible to the operator.
         */

        if (string.IsNullOrWhiteSpace(
                activity.ResourceName)
            || string.Equals(
                activity.ResourceName,
                "<unknown resource>",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (activity.ActivityTimestamp
            == DateTimeOffset.MinValue)
        {
            return BackupReportEntryClassification.Unknown;
        }

        if (string.IsNullOrWhiteSpace(
                activity.State))
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * Any latest activity that has not completed is operationally
         * relevant and must remain visible.
         */
        if (!string.Equals(
                activity.State,
                "completed",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return BackupReportEntryClassification.NeedsReview;
        }

        /*
         * A completed activity without a result cannot safely be called
         * healthy.
         */
        if (string.IsNullOrWhiteSpace(
                activity.ResultCode))
        {
            return BackupReportEntryClassification.Unknown;
        }

        /*
         * This is the only condition currently allowed to disappear from
         * detailed output.
         */
        if (string.Equals(
                activity.ResultCode,
                "ok",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return BackupReportEntryClassification.Healthy;
        }

        /*
         * Acronis production data has already shown:
         *
         *   state       = completed
         *   result.code = warning
         *
         * Any completed activity whose result is not "ok" deserves
         * operator attention.
         */
        return BackupReportEntryClassification.NeedsReview;
    }
}