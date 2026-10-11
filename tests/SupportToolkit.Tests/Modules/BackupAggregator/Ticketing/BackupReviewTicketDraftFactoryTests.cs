using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Tests.Modules.BackupAggregator.Ticketing;

public class BackupReviewTicketDraftFactoryTests
{
    [Fact]
    public void Create_ConvertsCompleteFixtureReportIntoTicketDraft()
    {
        var report =
            new BackupAggregatorFixtureFactory()
                .BuildReport();

        var factory =
            new BackupReviewTicketDraftFactory();

        var draft =
            factory.Create(
                report
            );

        Assert.Equal(
            "Backup review - 3 tenants require attention",
            draft.Subject
        );

        Assert.Contains(
            "Tenants reviewed: 5/5",
            draft.Body
        );

        Assert.Contains(
            "Backup resources checked: 15",
            draft.Body
        );

        Assert.Contains(
            "Healthy: 6",
            draft.Body
        );

        Assert.Contains(
            "Require attention: 7",
            draft.Body
        );

        Assert.Contains(
            "Unclassified: 2",
            draft.Body
        );

        Assert.Contains(
            "ATTENTION REQUIRED",
            draft.Body
        );

        Assert.Contains(
            "Alpine Consulting",
            draft.Body
        );

        Assert.Contains(
            "operations@alpine.example",
            draft.Body
        );

        Assert.Contains(
            "Status: Protection conflict",
            draft.Body
        );

        Assert.Contains(
            "EXAMPLE-SQL-01",
            draft.Body
        );

        Assert.Contains(
            "Status: Not protected",
            draft.Body
        );

        Assert.Contains(
            "UNCLASSIFIED DATA",
            draft.Body
        );

        Assert.Contains(
            "Adventure Works",
            draft.Body
        );

        Assert.DoesNotContain(
            "alex@northwind.example",
            draft.Body
        );

        Assert.Contains(
            "6 healthy backup resources were checked and suppressed from ticket details.",
            draft.Body
        );
    }

    [Fact]
    public void Create_RejectsIncompleteTenantCoverage()
    {
        var completeReport =
            new BackupAggregatorFixtureFactory()
                .BuildReport();

        var incompleteReport =
            new AggregatedBackupReport
            {
                Tenants =
                    completeReport.Tenants,

                Failures =
                [
                    new TenantBackupReviewFailure
                    {
                        TenantName =
                            "Example Failed Tenant",

                        Stage =
                            "device_inventory",

                        Reason =
                            "Synthetic failure"
                    }
                ]
            };

        var factory =
            new BackupReviewTicketDraftFactory();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    factory.Create(
                        incompleteReport
                    )
            );

        Assert.Contains(
            "not reviewed completely",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Create_RejectsIncompleteResourceAccounting()
    {
        var invalidReport =
            new AggregatedBackupReport
            {
                Tenants =
                [
                    new TenantBackupReport
                    {
                        TenantName =
                            "Example Tenant",

                        Entries =
                        [
                            new BackupReportEntry
                            {
                                ResourceName =
                                    "EXAMPLE-RESOURCE",

                                LastResult =
                                    "unknown",

                                Classification =
                                    (BackupReportEntryClassification)999
                            }
                        ]
                    }
                ]
            };

        var factory =
            new BackupReviewTicketDraftFactory();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    factory.Create(
                        invalidReport
                    )
            );

        Assert.Contains(
            "does not account for every backup resource",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }
}