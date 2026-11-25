using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.OsTicket.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Composition;

/// <summary>
/// `configure lab`: one profile for the second-provider lab, beside the
/// operator's own profiles, never instead of them.
/// </summary>
public class LabProfileTests
{
    private static MemorySecrets LabSecrets() =>
        new(new Dictionary<string, string>
        {
            [OsTicketConnections.ApiKey("lab").Key] = "EXAMPLE-LAB-1",
            [DuplicatiConnections.ServerPassword("lab", "machine-a").Key] = "EXAMPLE-LAB-2",
            [DuplicatiConnections.ServerPassword("lab", "machine-b").Key] = "EXAMPLE-LAB-3",
            [DolibarrConnections.ApiKey("lab").Key] = "EXAMPLE-LAB-4"
        });

    [Fact]
    public async Task TheLabProfile_SelectsTheThreeLabSystems_AndResolvesWithTheirSecrets()
    {
        var configuration = FullConfiguration();

        Assert.False(LabProfileComposition.Apply(configuration));
        configuration.ActiveProfile = "lab";

        var (_, _, resolver) = Create(configuration, LabSecrets());

        var ticketing = await TicketingComposition.ResolveTargetAsync(resolver);
        Assert.Equal("osTicket", ticketing.Destination.SystemName);
        Assert.Equal("127.0.0.1", ticketing.Destination.InstanceHost);
        Assert.Equal("10", ticketing.Destination.RoutingId);

        var fleet = await DuplicatiConnections.Instance.ResolveAsync(resolver, CancellationToken.None);
        Assert.Equal(["Contoso", "Northwind Traders"], fleet.Customers.Select(customer => customer.Name));

        var dolibarr = await DolibarrConnections.Instance.ResolveAsync(resolver, CancellationToken.None);
        Assert.Equal("http://127.0.0.1:8090/api/index.php/", dolibarr.ApiRoot.ToString());

        Assert.True(await resolver.IsMonitoringScopeEnabledAsync());
        Assert.Equal(SupportToolkitMode.Production, await resolver.GetModuleModeAsync("backup-aggregator"));
    }

    [Fact]
    public void TheOperatorsOwnProfilesAndConnections_AreLeftAlone()
    {
        var configuration = FullConfiguration();

        OsTicketConnections.Write(
            configuration,
            new Dictionary<string, OsTicketConnectionConfiguration>
            {
                ["office"] = new() { BaseUrl = "https://helpdesk.example.com/" }
            });

        var before = configuration.Profiles["work"];

        LabProfileComposition.Apply(configuration);

        Assert.Equal("work", configuration.ActiveProfile);
        Assert.Same(before, configuration.Profiles["work"]);
        Assert.Equal("acr", configuration.Profiles["work"].AcronisConnection);
        Assert.Equal(
            ["lab", "office"],
            OsTicketConnections.Read(configuration).Keys.Order());
    }

    [Fact]
    public void ApplyingAgain_ResetsTheLabProfile()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);
        configuration.Profiles["lab"].Backup = null;

        Assert.True(LabProfileComposition.Apply(configuration));
        Assert.Equal("duplicati", configuration.Profiles["lab"].Backup!.Provider);
    }

    [Fact]
    public async Task OnlyTheLabsSecrets_AreOfferedForTheLab()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);

        var missing =
            await RequiredSecrets.FindMissingAsync(
                configuration,
                new MemorySecrets([]),
                ProviderConnectionsComposition.All);

        var lab = missing.Where(LabProfileComposition.IsLabSecret).ToArray();

        Assert.Equal(4, lab.Length);
        Assert.Contains(missing, secret => secret.Provider == "Acronis");
        Assert.DoesNotContain(lab, secret => secret.Provider == "Acronis");
    }

    [Fact]
    public void TheSystemsLine_NamesWhatEachProfileTalksTo()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);

        Assert.Equal(
            new ActiveSystemsStatus("Acronis", "Business Central", "Zendesk"),
            ConfigurationStatusService.ActiveSystems(configuration.Profiles["work"]));

        Assert.Equal(
            "backup Duplicati · commercial Dolibarr · tickets osTicket",
            ConfigurationStatusService.ActiveSystems(configuration.Profiles["lab"]).ToString());

        Assert.Equal(
            new ActiveSystemsStatus("none", "none", "none"),
            ConfigurationStatusService.ActiveSystems(new SupportToolkitProfile()));
    }

    [Fact]
    public async Task Status_CarriesTheActiveProfilesSystems()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);
        configuration.ActiveProfile = "lab";

        var (service, _, _) = Create(configuration, LabSecrets());

        Assert.Equal(
            new ActiveSystemsStatus("Duplicati", "Dolibarr", "osTicket"),
            (await service.GetAsync()).ActiveSystems);
    }
}
