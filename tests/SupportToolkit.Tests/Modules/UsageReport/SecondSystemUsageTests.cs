using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Production;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Subscriptions;
using SupportToolkit.Providers.Dolibarr.Transport;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Zammad.Configuration;
using SupportToolkit.Tests.Providers.Dolibarr;
using SupportToolkit.Tests.Providers.Duplicati;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Modules.UsageReport;

/// <summary>
/// The usage report follows the profile's backup and commercial settings,
/// as the Backup Aggregator does: Duplicati storage against Dolibarr
/// billing in the lab. Every value is fictional.
/// </summary>
public class SecondSystemUsageTests
{
    private const string Finished = "20261120T115523Z";

    private static Dictionary<string, string> Completed(long? bytes) =>
        bytes is null
            ? new() { ["LastBackupFinished"] = Finished, ["LastBackupDate"] = Finished }
            : new()
            {
                ["LastBackupFinished"] = Finished,
                ["LastBackupDate"] = Finished,
                ["TargetFilesSize"] = bytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

    private static readonly FakeDuplicatiServer.Run Success =
        new("Success", "2026-11-20T11:55:23Z");

    private static (DuplicatiUsageSource Source, string TenantId) Duplicati(FakeDuplicatiServer server)
    {
        var customer =
            new ResolvedDuplicatiCustomer(
                "Customer A",
                [new ResolvedDuplicatiServer("machine-a", new Uri("https://a.example.com/"), FakeDuplicatiServer.Password)]);

        var fleet = new ResolvedDuplicatiFleet("lab", [customer]);

        return (
            new DuplicatiUsageSource(fleet, new DuplicatiServerReader(new HttpClient(server))),
            fleet.CustomerId(customer));
    }

    [Fact]
    public async Task Duplicati_NamesItsCustomersAsTheReviewDoes()
    {
        var (source, id) = Duplicati(new FakeDuplicatiServer());

        var tenants = await source.GetCustomerTenantsAsync(CancellationToken.None);

        var tenant = Assert.Single(tenants.Tenants);

        Assert.Equal("duplicati:lab:customer a", id);
        Assert.Equal(id, tenant.Id);
        Assert.Equal("Customer A", tenant.Name);
    }

    [Fact]
    public async Task Duplicati_AddsUpCompletedBackups_AndLeavesOutJobsThatNeverCompleted()
    {
        var server =
            new FakeDuplicatiServer()
                .AddJob("1", "Documents", Completed(4519), Success)
                .AddJob("2", "Photos", Completed(1000), Success)
                .AddJob("3", "Never run");

        var (source, id) = Duplicati(server);

        var storage = await source.GetStorageAsync([id], CancellationToken.None);
        var figures = storage.ByTenant[id];

        Assert.Equal(5519m, figures.Total.Bytes);
        Assert.Equal(figures.Total, figures.Backup);
        Assert.False(figures.Microsoft365.IsKnown);
        Assert.Equal(DuplicatiUsageSource.NoMicrosoft365, figures.Microsoft365.Problem);
        Assert.Contains(storage.Notices, notice => notice.StartsWith("1 Duplicati job(s) have never completed", StringComparison.Ordinal));
        Assert.Contains(DuplicatiUsageSource.Caveat, storage.Notices);

        // Counts only: no customer, job or server name in a notice.
        Assert.DoesNotContain(storage.Notices, notice => notice.Contains("Customer A") || notice.Contains("Never run"));
    }

    [Fact]
    public async Task Duplicati_ACompletedJobWithoutASize_MakesTheFigureUnknown_NeverZero()
    {
        var server =
            new FakeDuplicatiServer()
                .AddJob("1", "Documents", Completed(4519), Success)
                .AddJob("2", "Photos", Completed(null), Success);

        var (source, id) = Duplicati(server);

        var figures = (await source.GetStorageAsync([id], CancellationToken.None)).ByTenant[id];

        Assert.False(figures.Total.IsKnown);
        Assert.False(figures.Total.NotReported);
        Assert.Contains("no size", figures.Total.Problem);
    }

    [Fact]
    public async Task Duplicati_NoCompletedBackup_IsNothingReportedYet()
    {
        var (source, id) = Duplicati(new FakeDuplicatiServer().AddJob("3", "Never run"));

        var figures = (await source.GetStorageAsync([id], CancellationToken.None)).ByTenant[id];

        Assert.True(figures.Total.NotReported);
        Assert.Null(figures.Total.Bytes);
    }

    [Fact]
    public async Task Duplicati_AServerThatCannotBeRead_MakesTheFigureUnknown()
    {
        var (source, id) =
            Duplicati(new FakeDuplicatiServer { FailEverythingWith = System.Net.HttpStatusCode.InternalServerError });

        var figures = (await source.GetStorageAsync([id], CancellationToken.None)).ByTenant[id];

        Assert.False(figures.Total.IsKnown);
        Assert.Contains("machine-a", figures.Total.Problem);
    }

    [Fact]
    public async Task Dolibarr_ContractLines_AreBillingLines_NumberedLikeTheEvidence()
    {
        var dolibarr = new FakeDolibarr();

        var backup = FakeDolibarr.Line("2", "EXAMPLE-BACKUP", "4", qty: "250");
        backup["subprice"] = "10.00000000";
        backup["remise_percent"] = "5";
        backup["total_ht"] = "2375.00000000";

        var unreadable = FakeDolibarr.Line("3", "EXAMPLE-BACKUP", "4", qty: "x");
        unreadable["total_ht"] = "";

        dolibarr.Contracts.Add(
            FakeDolibarr.Contract("1", "CT-1", "7", "1", backup, unreadable, FakeDolibarr.Line("4", null, "4")));

        var lines =
            await new DolibarrBillingLineSource(
                    new ResolvedDolibarrConnection("lab", FakeDolibarr.ApiRoot, FakeDolibarr.ApiKey),
                    () => DolibarrHttpPipeline.Create(dolibarr))
                .GetBillingLinesAsync();

        Assert.Equal(2, lines.Count);

        var first = lines[0];

        Assert.Equal("CT-1/2", first.SubscriptionNumber);
        Assert.Equal(2, first.LineNumber);
        Assert.Equal("EXAMPLE-BACKUP", first.ProductRef);
        Assert.Equal(250m, first.Quantity);
        Assert.Equal(10m, first.UnitPrice);
        Assert.Equal(5m, first.DiscountPercent);
        Assert.Equal(2375m, first.Amount);

        // Unreadable is missing, never zero.
        Assert.Null(lines[1].Quantity);
        Assert.Null(lines[1].Amount);
    }

    [Fact]
    public async Task TheLabProfile_ReadsDuplicatiAndDolibarr()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);
        configuration.ActiveProfile = LabProfileComposition.ProfileName;

        var (_, _, resolver) = Create(configuration, LabSecrets());

        var availability =
            await UsageReportComposition.Workflow(resolver).GetAvailabilityAsync();

        Assert.Equal(SupportToolkitMode.Production, availability.Mode);
        Assert.True(availability.IsAvailable, availability.Reason);
    }

    [Fact]
    public async Task ABackupSystemTheReportCannotRead_IsReported_NeverReplaced()
    {
        var configuration = FullConfiguration();
        configuration.Profiles["work"].Modules["usage-report"] = SupportToolkitMode.Production;
        configuration.Profiles["work"].MonitoringScope["usage-report"] =
            new ModuleMonitoringScopeConfiguration { ServiceItemNumbers = ["EXAMPLE-BACKUP"] };
        configuration.Profiles["work"].Backup =
            new BackupSelectionConfiguration { Provider = "no-such-system" };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var availability =
            await UsageReportComposition.Workflow(resolver).GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("no-such-system", availability.Reason);
    }

    private static MemorySecrets LabSecrets() =>
        new(new Dictionary<string, string>
        {
            [ZammadConnections.ApiToken("lab").Key] = "EXAMPLE-LAB-1",
            [ZammadConnections.DeletionToken("lab").Key] = "EXAMPLE-LAB-5",
            [DuplicatiConnections.ServerPassword("lab", "machine-a").Key] = "EXAMPLE-LAB-2",
            [DuplicatiConnections.ServerPassword("lab", "machine-b").Key] = "EXAMPLE-LAB-3",
            [DolibarrConnections.ApiKey("lab").Key] = "EXAMPLE-LAB-4"
        });
}
