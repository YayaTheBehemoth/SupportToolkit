using System.Text.Json;
using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Subscriptions;
using SupportToolkit.Providers.Dolibarr.Transport;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Duplicati.Transport;
using SupportToolkit.Tests.Providers.Dolibarr;
using SupportToolkit.Tests.Providers.Duplicati;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// Dolibarr as the monitored review's commercial system, selected like the
/// backup and ticketing systems. Every value is fictional.
/// </summary>
public class DolibarrMonitoringScopeTests
{
    private static SupportToolkitConfiguration DolibarrConfiguration()
    {
        var configuration = FullConfiguration();

        DolibarrConnections.Write(
            configuration,
            new Dictionary<string, DolibarrConnectionConfiguration>
            {
                ["erp"] = new() { BaseUrl = "https://erp.example.com/" }
            });

        // The Business Central field stays set: the block must win.
        configuration.Profiles["work"].Commercial =
            new CommercialSelectionConfiguration { Provider = "Dolibarr", Connection = "erp" };

        return configuration;
    }

    private static MemorySecrets Secrets(bool withKey = true)
    {
        var secrets = new Dictionary<string, string>();

        if (withKey)
        {
            secrets[DolibarrConnections.ApiKey("erp").Key] = FakeDolibarr.ApiKey;
        }

        return new MemorySecrets(secrets);
    }

    [Fact]
    public void TheOlderBusinessCentralField_StillSelectsIt_AndTheCommercialBlockWins()
    {
        Assert.Equal(
            new CommercialSelection("businesscentral", "bc"),
            CommercialSelection.For(FullConfiguration().Profiles["work"]));

        Assert.Equal(
            new CommercialSelection("dolibarr", "erp"),
            CommercialSelection.For(DolibarrConfiguration().Profiles["work"]));

        Assert.Null(CommercialSelection.For(new SupportToolkitProfile()));
    }

    [Fact]
    public async Task TheMonitoredReview_IsAvailable_WithDolibarrAlone()
    {
        var configuration = DolibarrConfiguration();
        configuration.Profiles["work"].BusinessCentralConnection = null;

        var (_, _, resolver) = Create(configuration, Secrets());

        Assert.True(await resolver.IsMonitoringScopeEnabledAsync());

        configuration.Profiles["work"].Commercial = null;

        Assert.False(await resolver.IsMonitoringScopeEnabledAsync());
    }

    [Fact]
    public async Task AConnection_ResolvesItsApiRootAndKey()
    {
        var (_, _, resolver) = Create(DolibarrConfiguration(), Secrets());

        var connection = await DolibarrConnections.Instance.ResolveAsync(resolver, CancellationToken.None);

        Assert.Equal(new Uri("https://erp.example.com/api/index.php/"), connection.ApiRoot);
        Assert.DoesNotContain(FakeDolibarr.ApiKey, connection.ToString());
    }

    [Fact]
    public async Task AMissingKey_NamesItsVariable()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(DolibarrConfiguration()),
                new EnvironmentSecretStore(_ => null),
                _ => null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DolibarrConnections.Instance.ResolveAsync(resolver, CancellationToken.None));

        Assert.Contains("SUPPORTTOOLKIT_SECRET__DOLIBARR__ERP__API_KEY", error.Message);
    }

    [Fact]
    public async Task TheKey_IsARequiredSecret()
    {
        var missing =
            await RequiredSecrets.FindMissingAsync(
                DolibarrConfiguration(),
                Secrets(withKey: false),
                ProviderConnectionsComposition.All);

        Assert.Contains(missing, entry => entry.Provider == "Dolibarr" && entry.ConnectionName == "erp");
    }

    [Theory]
    [InlineData("veeam-erp", "does not know")]
    [InlineData("", "names no provider")]
    public async Task AnUnknownOrMissingProvider_IsRefused(string provider, string expected)
    {
        var configuration = DolibarrConfiguration();
        configuration.Profiles["work"].Commercial!.Provider = provider;

        var (_, _, resolver) = Create(configuration, Secrets());

        var sources = CommercialSystemsComposition.ForWorkflow(resolver, ReviewRunMode.Manual, null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => sources.Evidence(CancellationToken.None));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task ACommercialBlockNamingAnotherBusinessCentralConnection_IsRefused()
    {
        var configuration = FullConfiguration();
        configuration.Profiles["work"].Commercial =
            new CommercialSelectionConfiguration { Provider = "businesscentral", Connection = "other" };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var sources = CommercialSystemsComposition.ForWorkflow(resolver, ReviewRunMode.Manual, null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(() => sources.Names(CancellationToken.None));

        Assert.Contains("businessCentralConnection is different", error.Message);
    }

    [Fact]
    public async Task TheComposition_ChoosesDolibarrSources()
    {
        var (_, _, resolver) = Create(DolibarrConfiguration(), Secrets());

        var sources = CommercialSystemsComposition.ForWorkflow(resolver, ReviewRunMode.Manual, null);

        Assert.IsType<DolibarrSubscriptionEvidenceSource>(await sources.Evidence(CancellationToken.None));
        Assert.IsType<DolibarrCustomerNameSource>(await sources.Names(CancellationToken.None));
    }

    [Fact]
    public async Task Status_ShowsDolibarr_AndNoLongerAsksForBusinessCentral()
    {
        var configuration = DolibarrConfiguration();
        configuration.Profiles["work"].BusinessCentralConnection = null;

        var (service, _, _) = Create(configuration, Secrets());

        var status = await service.GetAsync();
        var commercial = status.Runtime!.CommercialSystem!;

        Assert.Equal("Dolibarr monitoring scope", commercial.Name);
        Assert.Equal(ConnectionState.Valid, commercial.State);
        Assert.Equal("erp", commercial.Alias);
        Assert.Null(status.Runtime.BusinessCentral.Reason);
        Assert.DoesNotContain(FakeDolibarr.ApiKey, JsonSerializer.Serialize(status));
    }

    [Fact]
    public async Task Status_WithoutTheKey_IsInvalid_AndSaysWhy()
    {
        var (service, _, _) = Create(DolibarrConfiguration(), Secrets(withKey: false));

        var commercial = (await service.GetAsync()).Runtime!.CommercialSystem!;

        Assert.Equal(ConnectionState.Invalid, commercial.State);
        Assert.Contains("API key", commercial.Reason);
    }

    [Fact]
    public async Task ABusinessCentralProfile_HasNoSeparateCommercialStatus()
    {
        var (service, _, _) = Create(FullConfiguration(), FullSecrets());

        Assert.Null((await service.GetAsync()).Runtime!.CommercialSystem);
    }

    /// <summary>
    /// The whole chain on synthetic data: Dolibarr entitlement decides
    /// which Duplicati customers the review covers.
    /// </summary>
    [Fact]
    public async Task DolibarrEntitlement_DecidesWhichDuplicatiCustomersAreReviewed()
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",
                Profiles =
                {
                    ["work"] = new SupportToolkitProfile
                    {
                        Commercial = new() { Provider = "dolibarr", Connection = "erp" },
                        Modules = { ["backup-aggregator"] = SupportToolkitMode.Production },
                        MonitoringScope =
                        {
                            ["backup-aggregator"] = new ModuleMonitoringScopeConfiguration
                            {
                                MonitoringItemNumber = "EXAMPLE-MONITORING",
                                ServiceItemNumbers = ["EXAMPLE-BACKUP"]
                            }
                        }
                    }
                }
            };

        var dolibarr = new FakeDolibarr();
        dolibarr.Customers.Add(FakeDolibarr.Customer("1", "Northwind Traders ApS", "CU-1"));
        dolibarr.Customers.Add(FakeDolibarr.Customer("2", "Contoso A/S", "CU-2"));
        dolibarr.Contracts.Add(FakeDolibarr.Contract("1", "CT-1", "1", "1",
            FakeDolibarr.Line("1", "EXAMPLE-MONITORING", "4"),
            FakeDolibarr.Line("2", "EXAMPLE-BACKUP", "4")));
        dolibarr.Contracts.Add(FakeDolibarr.Contract("2", "CT-2", "2", "1",
            FakeDolibarr.Line("3", "EXAMPLE-BACKUP", "4"),
            FakeDolibarr.Line("4", "EXAMPLE-MONITORING", "5", endReal: 1790000000)));

        var connection = new ResolvedDolibarrConnection("erp", FakeDolibarr.ApiRoot, FakeDolibarr.ApiKey);

        var fleet =
            new ResolvedDuplicatiFleet(
                "lab",
                [
                    new("Northwind Traders", [new("nw-1", new Uri("https://nw-1.example.com/"), FakeDuplicatiServer.Password)]),
                    new("Contoso", [new("co-1", new Uri("https://co-1.example.com/"), FakeDuplicatiServer.Password)])
                ]);

        var workflow =
            BackupAggregatorComposition.ScopedWorkflow(
                new SupportToolkitConfigurationResolver(
                    new MemoryStore(configuration),
                    new MemorySecrets([]),
                    _ => null),
                evidenceSourceFactory: _ =>
                    Task.FromResult<ISmartSubscriptionEvidenceSource>(
                        new DolibarrSubscriptionEvidenceSource(connection, () => DolibarrHttpPipeline.Create(dolibarr))),
                inventoryRuntimeFactory: _ =>
                    Task.FromResult(
                        new ScopedInventoryRuntime(
                            new BackupInventoryReviewService(
                                new DuplicatiBackupInventorySource(
                                    fleet,
                                    new DuplicatiServerReader(
                                        DuplicatiHttpPipeline.Create(
                                            new FakeDuplicatiServer().AddJob("1", "Documents"))))),
                            null)),
                nameSourceFactory: _ =>
                    Task.FromResult<ICustomerNameSource>(
                        new DolibarrCustomerNameSource(connection, () => DolibarrHttpPipeline.Create(dolibarr))));

        var result = await workflow.ReviewAsync(new DateOnly(2026, 11, 20));

        Assert.Equal("Northwind Traders", Assert.Single(result.Selection.InventoryCandidates).Name);
        Assert.Equal(1, result.Selection.NotEntitledCount);
        Assert.Equal("Northwind Traders", Assert.Single(result.Inventory.Tenants).TenantName);
    }
}
