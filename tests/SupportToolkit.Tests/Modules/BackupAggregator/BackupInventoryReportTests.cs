using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupInventoryReportTests
{
    [Fact]
    public void AggregatedReport_AccountsForEveryClassification()
    {
        var tenant =
            new TenantBackupReport
            {
                TenantName = "Tenant",
                Entries =
                [
                    CreateEntry(
                        BackupReportEntryClassification.Healthy
                    ),
                    CreateEntry(
                        BackupReportEntryClassification.NeedsReview
                    ),
                    CreateEntry(
                        BackupReportEntryClassification.Unknown
                    )
                ]
            };

        var report =
            new AggregatedBackupReport
            {
                Tenants = [tenant]
            };

        Assert.Equal(
            3,
            report.RowsChecked
        );
        Assert.Equal(
            1,
            report.HealthyRowsSuppressed
        );
        Assert.Equal(
            1,
            report.FindingsCount
        );
        Assert.Equal(
            1,
            report.UnknownRowsCount
        );
        Assert.Equal(
            3,
            report.AccountedRows
        );
        Assert.True(
            report.IsFullyAccountedFor
        );
    }

    private static BackupReportEntry CreateEntry(
        BackupReportEntryClassification classification)
    {
        return new BackupReportEntry
        {
            DeviceName = "Resource",
            LastResult = "fixture",
            Classification = classification
        };
    }
}
