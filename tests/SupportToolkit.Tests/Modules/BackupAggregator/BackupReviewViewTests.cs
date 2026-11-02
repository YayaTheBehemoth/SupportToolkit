using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Presentation;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// The views are what every interface other than the console renders, so
/// they must say exactly what the report says: no number, status or
/// completeness is derived anywhere else.
/// </summary>
public class BackupReviewViewTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static async Task<ScopedBackupInventoryReviewResult> FixtureReview()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new Store(new SupportToolkitConfiguration()),
                new Secrets(),
                _ => null);

        return await new BackupAggregatorScopedWorkflow(resolver)
            .ReviewAsync(Today);
    }

    private static BackupReportEntry Entry(
        string name,
        BackupReportEntryClassification classification,
        string? state = "completed",
        string result = "ok") =>
        new()
        {
            ResourceName = name,
            LastResult = result,
            Classification = classification,
            ResourceState = state,
            PlanName = "Plan A",
            LastBackupRun = new DateTimeOffset(2026, 10, 14, 3, 30, 0, TimeSpan.Zero)
        };

    private static AggregatedBackupReport Report(
        IReadOnlyList<TenantBackupReport> tenants,
        IReadOnlyList<TenantBackupReviewFailure>? failures = null) =>
        new()
        {
            Tenants = tenants,
            Failures = failures ?? []
        };

    // ---- The fixture review: the numbers are the report's ----

    [Fact]
    public async Task TheFixtureReview_HasTheSameNumbersAsTheReport()
    {
        var review = await FixtureReview();
        var report = review.Inventory;

        var view = BackupReviewView.From(report);

        Assert.Equal(report.TenantsAttempted, view.TenantsAttempted);
        Assert.Equal(report.TenantCount, view.TenantsReviewed);
        Assert.Equal(report.FailedTenantCount, view.TenantsFailed);
        Assert.Equal(report.RowsChecked, view.ResourcesChecked);
        Assert.Equal(report.AccountedRows, view.ResourcesAccounted);
        Assert.Equal(report.HealthyRowsSuppressed, view.Healthy);
        Assert.Equal(report.FindingsCount, view.RequireAttention);
        Assert.Equal(report.UnknownRowsCount, view.Unclassified);
        Assert.Equal(
            view.RequireAttention,
            view.Attention.Sum(tenant => tenant.Entries.Count));
        Assert.Equal(
            view.Unclassified,
            view.UnclassifiedTenants.Sum(tenant => tenant.Entries.Count));
    }

    [Fact]
    public async Task EveryResourceStatus_IsTheConsolesStatus()
    {
        var report = (await FixtureReview()).Inventory;

        var view = BackupReviewView.From(report);

        var expected =
            report.Tenants
                .SelectMany(tenant => tenant.Findings)
                .Select(BackupReportEntryPresentation.GetDisplayStatus)
                .Order()
                .ToArray();

        var actual =
            view.Attention
                .SelectMany(tenant => tenant.Entries)
                .Select(entry => entry.Status)
                .Order()
                .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TheCompleteness_IsTheReviewsCompleteness()
    {
        var review = await FixtureReview();

        var completeness = CompletenessView.From(review);

        Assert.Equal(review.IsInventoryComplete, completeness.InventoryComplete);
        Assert.Equal(review.IsScopeComplete, completeness.ScopeComplete);
        Assert.Equal(review.IsOverallComplete, completeness.OverallComplete);
        Assert.Equal(review.TicketBlockers, completeness.Blockers);
    }

    [Fact]
    public async Task TheScopePartition_IsTheReportsPartition()
    {
        var review = await FixtureReview();
        var scope = ScopeView.From(review);

        Assert.NotNull(scope);

        var report = review.ScopeReport!;

        Assert.Equal(report.InScope.Count, scope.InScope.Count);
        Assert.Equal(report.Excluded.Count, scope.Excluded.Count);
        Assert.Equal(report.NeedsReview.Count, scope.NeedsReview.Count);
        Assert.Equal(report.UnscopedTenants.Count, scope.Unmatched.Count);
        Assert.Equal(
            report.AcronisCustomerTenants,
            scope.InScope.Count(tenant => !tenant.IsOperatorTenant)
            + scope.Excluded.Count
            + scope.NeedsReview.Count
            + scope.Unmatched.Count);
    }

    [Fact]
    public async Task AReviewWithoutAScopeReport_HasNoScopeView()
    {
        var review = (await FixtureReview()) with { ScopeReport = null };

        Assert.Null(ScopeView.From(review));
    }

    // ---- Shapes the fixture does not produce ----

    [Fact]
    public void AnUnclassifiedResource_CarriesWhyItWasNotClassified()
    {
        var report =
            Report(
            [
                new TenantBackupReport
                {
                    TenantName = "Tenant A",
                    Entries =
                    [
                        Entry(
                            "disk-1",
                            BackupReportEntryClassification.Unknown,
                            state: "completed",
                            result: "<missing>")
                    ]
                }
            ]);

        var view = BackupReviewView.From(report);

        var entry = Assert.Single(
            Assert.Single(view.UnclassifiedTenants).Entries);

        Assert.Equal("Result code missing", entry.UnclassifiedReason);
        Assert.Equal(InventoryTrust.Complete, view.Trust);
        Assert.Empty(view.Attention);
    }

    [Fact]
    public void AFailedTenant_MakesTheReviewPartial_AndIsListed()
    {
        var report =
            Report(
                [
                    new TenantBackupReport
                    {
                        TenantName = "Tenant A",
                        Entries =
                        [
                            Entry("disk-1", BackupReportEntryClassification.Healthy)
                        ]
                    }
                ],
                [
                    new TenantBackupReviewFailure
                    {
                        TenantName = "Tenant B",
                        Stage = "Inventory",
                        Reason = "Request failed."
                    }
                ]);

        var view = BackupReviewView.From(report);

        Assert.Equal(InventoryTrust.PartialTenants, view.Trust);
        Assert.Equal(2, view.TenantsAttempted);
        Assert.Equal(1, view.TenantsFailed);

        var failure = Assert.Single(view.Incomplete);

        Assert.Equal("Tenant B", failure.Tenant);
        Assert.Equal("Request failed.", failure.Reason);
    }

    [Fact]
    public void NothingReviewed_IsStated_NotShownAsHealthy()
    {
        var view = BackupReviewView.From(Report([]));

        Assert.True(view.NothingReviewed);
        Assert.Equal(0, view.Healthy);
    }

    [Fact]
    public void Tenants_AreSortedByName_LikeTheConsole()
    {
        var report =
            Report(
            [
                Tenant("Zulu"),
                Tenant("alpha"),
                Tenant("Mike")
            ]);

        var view = BackupReviewView.From(report);

        Assert.Equal(
            ["alpha", "Mike", "Zulu"],
            view.Attention.Select(tenant => tenant.Tenant).ToArray());

        static TenantBackupReport Tenant(string name) =>
            new()
            {
                TenantName = name,
                Entries =
                [
                    Entry("disk", BackupReportEntryClassification.NeedsReview,
                        state: "idle", result: "no_successful_backup")
                ]
            };
    }

    [Fact]
    public void ThePendingInProgressResources_AreReported()
    {
        var report =
            new AggregatedBackupReport
            {
                Tenants = [],
                InProgressWait =
                    new InProgressWaitSummary(
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(5),
                        3,
                        PendingResources: 4,
                        PendingTenants: 2,
                        TimedOut: true)
            };

        Assert.Equal(4, BackupReviewView.From(report).InProgressPending);
    }

    private sealed class Secrets : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("fixture-secret");

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
