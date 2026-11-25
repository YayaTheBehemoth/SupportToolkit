using System.Net;
using System.Text.Json;
using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Domain.Tenants;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Duplicati.Transport;
using SupportToolkit.Tests.Providers.Duplicati;
using static SupportToolkit.Tests.Providers.Duplicati.FakeDuplicatiServer;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// Duplicati behind the Backup Aggregator: a fleet connection whose
/// customers are tenants and whose servers' jobs are resources. Every
/// value is fictional.
/// </summary>
public class DuplicatiBackupTests
{
    private static SupportToolkitConfiguration FleetConfiguration()
    {
        var configuration = FullConfiguration();

        DuplicatiConnections.Write(
            configuration,
            new Dictionary<string, DuplicatiConnectionConfiguration>
            {
                ["lab"] = new()
                {
                    Customers =
                    {
                        ["Customer A"] = new()
                        {
                            Servers =
                            {
                                ["machine-a"] = new() { Url = "https://a.example.com/" },
                                ["machine-c"] = new() { Url = "https://c.example.com/" }
                            }
                        },
                        ["Customer B"] = new()
                        {
                            Servers = { ["machine-b"] = new() { Url = "https://b.example.com/" } }
                        }
                    }
                }
            });

        configuration.Profiles["work"].Backup =
            new BackupSelectionConfiguration { Provider = "Duplicati", Connection = "lab" };

        return configuration;
    }

    private static MemorySecrets FleetSecrets(params string[] withoutPasswordFor)
    {
        var secrets = new Dictionary<string, string>();

        foreach (var alias in new[] { "machine-a", "machine-b", "machine-c" }.Except(withoutPasswordFor))
        {
            secrets[DuplicatiConnections.ServerPassword("lab", alias).Key] = Password;
        }

        return new MemorySecrets(secrets);
    }

    private static void EditFleet(
        SupportToolkitConfiguration configuration,
        Action<DuplicatiConnectionConfiguration> edit)
    {
        var connections =
            new Dictionary<string, DuplicatiConnectionConfiguration>(
                DuplicatiConnections.Read(configuration));

        edit(connections["lab"]);
        DuplicatiConnections.Write(configuration, connections);
    }

    // ---------------------------------------------------------------
    // Selection and settings
    // ---------------------------------------------------------------

    [Fact]
    public void TheOlderAcronisField_StillSelectsAcronis_AndTheBackupBlockWins()
    {
        Assert.Equal(
            new BackupSelection("acronis", "acr"),
            BackupSelection.For(FullConfiguration().Profiles["work"]));

        Assert.Equal(
            new BackupSelection("duplicati", "lab"),
            BackupSelection.For(FleetConfiguration().Profiles["work"]));

        Assert.Null(BackupSelection.For(new SupportToolkitProfile()));
    }

    [Fact]
    public async Task AFleet_ResolvesEveryCustomerAndServer_WithItsPassword()
    {
        var (_, _, resolver) = Create(FleetConfiguration(), FleetSecrets());

        var fleet = await DuplicatiConnections.Instance.ResolveAsync(resolver, CancellationToken.None);

        Assert.Equal("lab", fleet.ConnectionName);
        Assert.Equal(["Customer A", "Customer B"], fleet.Customers.Select(customer => customer.Name));
        Assert.Equal(
            ["machine-a", "machine-c"],
            fleet.Customers[0].Servers.Select(server => server.Alias));
        Assert.All(fleet.Customers.SelectMany(customer => customer.Servers),
            server => Assert.DoesNotContain(Password, server.ToString()));
    }

    [Fact]
    public async Task AMissingPassword_NamesThatServersVariable()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(FleetConfiguration()),
                new EnvironmentSecretStore(name =>
                    name.EndsWith("MACHINE_C") ? null : Password),
                _ => null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DuplicatiConnections.Instance.ResolveAsync(resolver, CancellationToken.None));

        Assert.Contains("password of server 'machine-c'", error.Message);
        Assert.Contains("SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_C", error.Message);
    }

    public static TheoryData<Action<SupportToolkitConfiguration>, string> BrokenFleets() =>
        new()
        {
            { c => c.Profiles["work"].Backup!.Connection = null, "names no connection" },
            { c => c.Profiles["work"].Backup!.Connection = "missing", "does not exist" },
            { c => EditFleet(c, fleet => fleet.Customers.Clear()), "lists no customers" },
            { c => EditFleet(c, fleet => fleet.Customers["Customer B"].Servers.Clear()), "lists no servers" },
            { c => EditFleet(c, fleet => fleet.Customers["Customer B"].Servers["machine-a"] = new() { Url = "https://d.example.com/" }), "more than once" },
            { c => EditFleet(c, fleet => fleet.Customers["Customer B"].Servers["machine-b"].Url = "http://b.example.com/"), "HTTPS" },
            { c => EditFleet(c, fleet => fleet.Customers["Customer B"].Servers["bad alias!"] = new() { Url = "https://e.example.com/" }), "not a valid Duplicati server alias" }
        };

    [Theory]
    [MemberData(nameof(BrokenFleets))]
    public async Task AnIncompleteFleet_FailsLocally_AndSaysWhy(
        Action<SupportToolkitConfiguration> breakIt,
        string expected)
    {
        var configuration = FleetConfiguration();
        breakIt(configuration);

        var (_, _, resolver) = Create(configuration, FleetSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => DuplicatiConnections.Instance.ResolveAsync(resolver, CancellationToken.None));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain(Password, error.Message);
    }

    [Fact]
    public async Task EveryServersPassword_IsARequiredSecret()
    {
        var missing =
            await RequiredSecrets.FindMissingAsync(
                FleetConfiguration(),
                FleetSecrets("machine-b"),
                ProviderConnectionsComposition.All);

        var item = Assert.Single(missing, entry => entry.Provider == "Duplicati");
        Assert.Equal("password of server 'machine-b'", item.SecretName);
    }

    [Fact]
    public void TwoServersSharingAVariable_AreAClash()
    {
        var configuration = FleetConfiguration();
        EditFleet(configuration, fleet =>
            fleet.Customers["Customer B"].Servers["machine_a"] = new() { Url = "https://f.example.com/" });

        var clash =
            Assert.Single(
                SecretLocationClashes.Find(
                    configuration,
                    new EnvironmentSecretStore(_ => null),
                    ProviderConnectionsComposition.All));

        Assert.Equal("Duplicati", clash.Provider);
        Assert.Equal(["lab, server machine-a", "lab, server machine_a"], clash.ConnectionNames);
        Assert.Contains("read their password", clash.Describe());
    }

    // ---------------------------------------------------------------
    // Review
    // ---------------------------------------------------------------

    private static ResolvedDuplicatiFleet Fleet(params string[] aliases) =>
        new(
            "lab",
            [
                new ResolvedDuplicatiCustomer(
                    "Customer A",
                    aliases.Select(alias =>
                        new ResolvedDuplicatiServer(alias, new Uri($"https://{alias}.example.com/"), Password))
                    .ToArray())
            ]);

    private static DuplicatiBackupInventorySource Source(
        FakeDuplicatiServer server,
        params string[] aliases) =>
        new(Fleet(aliases.Length == 0 ? ["machine-a"] : aliases),
            new DuplicatiServerReader(DuplicatiHttpPipeline.Create(server)));

    private static async Task<IReadOnlyList<BackupReportEntry>> ReviewAsync(FakeDuplicatiServer server)
    {
        var source = Source(server);
        var tenant = await source.ResolveExactAsync("Customer A");
        return (await source.ReviewTenantAsync(tenant)).Report.Entries;
    }

    [Fact]
    public async Task CustomersAreTenants_FoundOnlyByTheirExactName()
    {
        var source = Source(new FakeDuplicatiServer());

        var tenant = Assert.Single(await source.GetCustomerTenantsAsync());
        Assert.Equal("Customer A", tenant.Name);
        Assert.Equal(TenantRole.Customer, tenant.Role);
        Assert.Equal("duplicati:lab:customer a", tenant.Id);
        Assert.Equal(tenant.Id, tenant.CanonicalId);

        Assert.Equal(tenant.Id, (await source.ResolveExactAsync(" Customer A ")).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ResolveExactAsync("customer a"));
    }

    [Fact]
    public async Task EachJob_IsAResource_ClassifiedLikeAcronisResources()
    {
        var entries =
            await ReviewAsync(
                new FakeDuplicatiServer()
                    .AddJob("1", "Documents",
                        new() { ["LastBackupFinished"] = "20261120T115204Z" },
                        new Run("Success", "2026-11-20T11:52:04Z"))
                    .AddJob("2", "Partly missing",
                        new() { ["LastBackupFinished"] = "20261120T115556Z" },
                        new Run("Warning", "2026-11-20T11:55:56Z"))
                    .AddJob("3", "Broken", new() { ["LastErrorDate"] = "20261120T115254Z" })
                    .AddJob("4", "Never run")
                    .AddJob("5", "Ambiguous",
                        new() { ["LastBackupFinished"] = "20261120T115204Z", ["LastErrorDate"] = "20261120T115204Z" },
                        new Run("Success", "2026-11-20T11:52:04Z")));

        Assert.Collection(
            entries,
            entry =>
            {
                Assert.Equal("machine-a: Documents", entry.ResourceName);
                Assert.Equal(BackupReportEntryClassification.Healthy, entry.Classification);
                Assert.Equal("Successful", entry.DisplayStatus);
            },
            entry =>
            {
                Assert.Equal(BackupReportEntryClassification.NeedsReview, entry.Classification);
                Assert.Equal("Completed with warnings", entry.DisplayStatus);
            },
            entry =>
            {
                Assert.Equal(BackupReportEntryClassification.NeedsReview, entry.Classification);
                Assert.Equal("Failed", entry.DisplayStatus);
            },
            entry =>
            {
                Assert.Equal(BackupReportEntryClassification.NeedsReview, entry.Classification);
                Assert.Equal("Never run", entry.DisplayStatus);
            },
            entry =>
            {
                Assert.Equal(BackupReportEntryClassification.Unknown, entry.Classification);
                Assert.Contains("same second", entry.UnclassifiedReason);
            });
    }

    [Theory]
    [InlineData(true, BackupReportEntryClassification.Healthy)]
    [InlineData(false, BackupReportEntryClassification.NeedsReview)]
    public async Task ARunningJob_IsHealthy_OnlyAfterAnEarlierSuccess(
        bool succeededBefore,
        BackupReportEntryClassification expected)
    {
        var server = new FakeDuplicatiServer { RunningJobId = "1" };

        if (succeededBefore)
        {
            server.AddJob("1", "Documents",
                new() { ["LastBackupFinished"] = "20261120T115204Z" },
                new Run("Success", "2026-11-20T11:52:04Z"));
        }
        else
        {
            server.AddJob("1", "Documents");
        }

        var entry = Assert.Single(await ReviewAsync(server));

        Assert.Equal(expected, entry.Classification);
        Assert.True(entry.IsInProgress);
        Assert.Equal("In progress", entry.DisplayStatus);
    }

    [Fact]
    public async Task AServerThatCannotBeRead_FailsTheTenant_NamingOnlyTheServer()
    {
        var source =
            Source(
                new FakeDuplicatiServer { FailEverythingWith = HttpStatusCode.BadGateway },
                "machine-a");

        var tenant = await source.ResolveExactAsync("Customer A");

        var error =
            await Assert.ThrowsAsync<TenantReviewStageException>(
                () => source.ReviewTenantAsync(tenant));

        Assert.Equal("Reading Duplicati server 'machine-a'", error.Stage);
    }

    [Fact]
    public async Task TheReviewCounts_AreDuplicatiJobs()
    {
        var source =
            Source(new FakeDuplicatiServer().AddJob("1", "Documents"), "machine-a", "machine-c");

        var result = await source.ReviewTenantAsync(await source.ResolveExactAsync("Customer A"));

        // The fake answers for both servers: two jobs read.
        Assert.Equal([new ResourceCount("Duplicati jobs", 2)], result.ResourceCounts);
    }

    [Fact]
    public async Task TheMonitoredReview_MatchesDuplicatiCustomers_ToCommercialEvidence()
    {
        const string MonitoringItem = "EXAMPLE-MONITORING";
        const string BackupItem = "EXAMPLE-BACKUP";

        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",
                Profiles =
                {
                    ["work"] = new SupportToolkitProfile
                    {
                        BusinessCentralConnection = "bc-work",
                        Modules = { ["backup-aggregator"] = SupportToolkitMode.Production },
                        MonitoringScope =
                        {
                            ["backup-aggregator"] = new ModuleMonitoringScopeConfiguration
                            {
                                MonitoringItemNumber = MonitoringItem,
                                ServiceItemNumbers = [BackupItem]
                            }
                        }
                    }
                }
            };

        var fleet =
            new ResolvedDuplicatiFleet(
                "lab",
                [
                    new ResolvedDuplicatiCustomer(
                        "Northwind Traders",
                        [new ResolvedDuplicatiServer("nw-1", new Uri("https://nw-1.example.com/"), Password)]),
                    new ResolvedDuplicatiCustomer(
                        "Contoso",
                        [new ResolvedDuplicatiServer("co-1", new Uri("https://co-1.example.com/"), Password)])
                ]);

        var server =
            new FakeDuplicatiServer().AddJob(
                "1", "Documents",
                new() { ["LastBackupFinished"] = "20261120T115204Z" },
                new Run("Success", "2026-11-20T11:52:04Z"));

        var workflow =
            BackupAggregatorComposition.ScopedWorkflow(
                new SupportToolkitConfigurationResolver(
                    new MemoryStore(configuration),
                    new MemorySecrets([]),
                    _ => null),
                evidenceSourceFactory: _ =>
                    Task.FromResult<SupportToolkit.Domain.MonitoringScope.ISmartSubscriptionEvidenceSource>(
                        new Evidence(
                            [
                                new("SUB-1", "BC-1", "Active", new DateOnly(2026, 1, 1), null, null, " "),
                                new("SUB-2", "BC-2", "Active", new DateOnly(2026, 1, 1), null, null, " ")
                            ],
                            [
                                new("SUB-1", MonitoringItem, "Item", false),
                                new("SUB-1", BackupItem, "Item", false),
                                new("SUB-2", BackupItem, "Item", false)
                            ])),
                inventoryRuntimeFactory: _ =>
                    Task.FromResult(
                        new ScopedInventoryRuntime(
                            new BackupInventoryReviewService(
                                new DuplicatiBackupInventorySource(
                                    fleet,
                                    new DuplicatiServerReader(DuplicatiHttpPipeline.Create(server)))),
                            null)),
                nameSourceFactory: _ =>
                    Task.FromResult<SupportToolkit.Domain.MonitoringScope.ICustomerNameSource>(
                        new Names(
                            new([
                                new("BC-1", "Northwind Traders ApS"),
                                new("BC-2", "Contoso A/S")
                            ], 0, 0))));

        var result = await workflow.ReviewAsync(new DateOnly(2026, 11, 20));

        var reviewed = Assert.Single(result.Selection.InventoryCandidates);
        Assert.Equal("Northwind Traders", reviewed.Name);
        Assert.Equal(1, result.Selection.NotEntitledCount);

        var tenant = Assert.Single(result.Inventory.Tenants);
        Assert.Equal("Northwind Traders", tenant.TenantName);
        Assert.Equal("nw-1: Documents", Assert.Single(tenant.Entries).ResourceName);
    }

    private sealed class Evidence(
        IReadOnlyList<SupportToolkit.Domain.MonitoringScope.SmartSubscriptionHeader> headers,
        IReadOnlyList<SupportToolkit.Domain.MonitoringScope.SmartSubscriptionLine> lines)
        : SupportToolkit.Domain.MonitoringScope.ISmartSubscriptionEvidenceSource
    {
        public Task<SupportToolkit.Domain.MonitoringScope.SmartSubscriptionEvidence> GetEvidenceAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SupportToolkit.Domain.MonitoringScope.SmartSubscriptionEvidence(headers, lines));
    }

    private sealed class Names(SupportToolkit.Domain.MonitoringScope.ScopedCustomerParties names)
        : SupportToolkit.Domain.MonitoringScope.ICustomerNameSource
    {
        public Task<SupportToolkit.Domain.MonitoringScope.ScopedCustomerParties> GetCustomerPartiesAsync(
            IReadOnlyCollection<string> customerNumbers,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(names);
    }

    // ---------------------------------------------------------------
    // Composition and status
    // ---------------------------------------------------------------

    [Fact]
    public async Task AnUnknownBackupProvider_IsRefused_NamingTheKnownOnes()
    {
        var configuration = FleetConfiguration();
        configuration.Profiles["work"].Backup!.Provider = "veeam";

        var (_, _, resolver) = Create(configuration, FleetSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BackupSystemsComposition.CreateRuntimeAsync(resolver, ReviewRunMode.Manual, null));

        Assert.Contains("'veeam'", error.Message);
        Assert.Contains("acronis, duplicati", error.Message);
    }

    [Fact]
    public async Task ABackupBlockNamingAnotherAcronisConnection_IsRefused()
    {
        var configuration = FullConfiguration();
        configuration.Profiles["work"].Backup =
            new BackupSelectionConfiguration { Provider = "acronis", Connection = "other" };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BackupSystemsComposition.CreateRuntimeAsync(resolver, ReviewRunMode.Manual, null));

        Assert.Contains("acronisConnection is different", error.Message);
    }

    [Fact]
    public async Task Status_ShowsTheFleet_AndNoLongerAsksForAcronisForTheReview()
    {
        var configuration = FleetConfiguration();
        configuration.Profiles["work"].AcronisConnection = null;

        var (service, _, _) = Create(configuration, FleetSecrets());

        var status = await service.GetAsync();
        var runtime = status.Runtime!;

        var backup = runtime.BackupSystem!;
        Assert.Equal("Duplicati backup review", backup.Name);
        Assert.Equal(ConnectionState.Valid, backup.State);
        Assert.Contains(backup.Details, detail => detail.Label == "Servers" && detail.Value == "3");

        Assert.Equal(ConnectionState.NotSelected, runtime.Acronis.State);
        Assert.Null(runtime.Acronis.Reason);

        var saved = Assert.Single(status.SavedConnections, connection => connection.Kind == "Duplicati");
        Assert.Equal(3, saved.Details.Count);
        Assert.DoesNotContain(Password, JsonSerializer.Serialize(status));
    }

    [Fact]
    public async Task Status_OfAFleetMissingAPassword_IsInvalid_AndSaysWhich()
    {
        var (service, _, _) = Create(FleetConfiguration(), FleetSecrets("machine-b"));

        var backup = (await service.GetAsync()).Runtime!.BackupSystem!;

        Assert.Equal(ConnectionState.Invalid, backup.State);
        Assert.Contains("machine-b", backup.Reason);
    }

    [Fact]
    public async Task AnAcronisProfile_HasNoSeparateBackupStatus()
    {
        var (service, _, _) = Create(FullConfiguration(), FullSecrets());

        Assert.Null((await service.GetAsync()).Runtime!.BackupSystem);
    }
}
