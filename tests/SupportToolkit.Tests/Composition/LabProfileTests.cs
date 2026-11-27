using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.OsTicket.Configuration;
using SupportToolkit.Providers.Zammad.Configuration;
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
            [ZammadConnections.ApiToken("lab").Key] = "EXAMPLE-LAB-1",
            [ZammadConnections.DeletionToken("lab").Key] = "EXAMPLE-LAB-5",
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
        Assert.Equal("Zammad", ticketing.Destination.SystemName);
        Assert.Equal("127.0.0.1", ticketing.Destination.InstanceHost);
        Assert.Equal("Users", ticketing.Destination.RoutingId);

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

        // Including an osTicket "lab" connection from before Zammad
        // replaced osTicket in the lab.
        OsTicketConnections.Write(
            configuration,
            new Dictionary<string, OsTicketConnectionConfiguration>
            {
                ["office"] = new() { BaseUrl = "https://helpdesk.example.com/" },
                ["lab"] = new() { BaseUrl = "http://127.0.0.1:8080/" }
            });

        var before = configuration.Profiles["work"];

        LabProfileComposition.Apply(configuration);

        Assert.Equal("work", configuration.ActiveProfile);
        Assert.Same(before, configuration.Profiles["work"]);
        Assert.Equal("acr", configuration.Profiles["work"].AcronisConnection);
        Assert.Equal(
            ["lab", "office"],
            OsTicketConnections.Read(configuration).Keys.Order());
        Assert.Equal(
            "http://127.0.0.1:8080/",
            OsTicketConnections.Read(configuration)["lab"].BaseUrl);
        Assert.Equal(["lab"], ZammadConnections.Read(configuration).Keys);
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

        // Zammad's API token and its separate deletion token, two Duplicati
        // passwords and the Dolibarr key.
        Assert.Equal(5, lab.Length);
        Assert.Contains(lab, secret => secret.Provider == "Zammad" && secret.SecretName == "deletion token");
        Assert.Contains(missing, secret => secret.Provider == "Acronis");
        Assert.DoesNotContain(lab, secret => secret.Provider == "Acronis");
    }

    [Fact]
    public async Task TheLabsBulkDelete_WorksOnItsZammad_WithTheDeletionToken()
    {
        var configuration = FullConfiguration();
        LabProfileComposition.Apply(configuration);
        configuration.ActiveProfile = "lab";

        var (_, _, resolver) = Create(configuration, LabSecrets());

        var availability =
            await BulkDeleteComposition.GetAvailabilityAsync(resolver, CancellationToken.None);

        Assert.True(availability.IsAvailable, availability.Reason);
        Assert.Equal("Zammad", availability.SystemName);
        Assert.Equal("127.0.0.1:8100", availability.DisplayInstance);
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
            "backup Duplicati · commercial Dolibarr · tickets Zammad",
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
            new ActiveSystemsStatus("Duplicati", "Dolibarr", "Zammad"),
            (await service.GetAsync()).ActiveSystems);
    }
}
