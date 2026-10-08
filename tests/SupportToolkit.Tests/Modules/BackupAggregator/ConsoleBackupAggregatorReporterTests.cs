using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Reporting;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class ConsoleBackupAggregatorReporterTests
{
    [Fact]
    public void Write_SuppressesHealthyDetailsAndShowsFinding()
    {
        var report =
            new AggregatedBackupReport
            {
                Tenants =
                [
                    new TenantBackupReport
                    {
                        TenantName = "Customer Alpha",
                        Entries =
                        [
                            new BackupReportEntry
                            {
                                DeviceName = "HEALTHY-SRV",
                                LastResult = "idle",
                                DeviceState = "idle",
                                Classification =
                                    BackupReportEntryClassification.Healthy
                            },
                            new BackupReportEntry
                            {
                                DeviceName = "SQL-SRV",
                                LastResult = "notProtected",
                                DeviceState = "notProtected",
                                PlanName =
                                    "SQL Database Backup (Disabled)",
                                Classification =
                                    BackupReportEntryClassification.NeedsReview
                            }
                        ]
                    }
                ]
            };

        var originalOut =
            Console.Out;

        try
        {
            using var writer =
                new StringWriter();

            Console.SetOut(
                writer
            );

            new ConsoleBackupAggregatorReporter()
                .Write(report);

            var output =
                writer.ToString();

            Assert.Contains(
                "SQL-SRV",
                output
            );
            Assert.Contains(
                "Not protected",
                output
            );
            Assert.DoesNotContain(
                "HEALTHY-SRV",
                output
            );
            Assert.Contains(
                "1 healthy backup resource",
                output
            );
        }
        finally
        {
            Console.SetOut(
                originalOut
            );
        }
    }
}
