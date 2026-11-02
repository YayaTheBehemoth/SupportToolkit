using System.Text.Json;
using SupportToolkit.Core.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Ui.Web;

public class ConfigurationStatusServiceTests
{
    [Fact]
    public async Task NoProfiles_IsReportedPlainly_WithNoRuntime()
    {
        var (service, _, _) = Create(new SupportToolkitConfiguration());

        var status = await service.GetAsync();

        Assert.False(status.HasProfiles);
        Assert.Null(status.ActiveProfile);
        Assert.Null(status.Runtime);
        Assert.Empty(status.Profiles);
        Assert.Equal("C:\\example\\local.config.json", status.ConfigurationPath);
    }

    [Fact]
    public async Task ACompleteProfile_ReportsEveryConnectionAsValid_LocallyOnly()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var runtime = (await service.GetAsync()).Runtime!;

        Assert.Equal(SupportToolkitMode.Production, runtime.BackupAggregatorMode.Mode);

        Assert.Equal(ConnectionState.Valid, runtime.Acronis.State);
        Assert.Equal(ConnectionState.Valid, runtime.ZendeskTicketing.State);
        Assert.Equal(ConnectionState.Valid, runtime.ZendeskDeletion.State);
        Assert.Equal(ConnectionState.Valid, runtime.BusinessCentral.State);
        Assert.True(runtime.ReviewWait.IsValid);
    }

    [Fact]
    public async Task OnlyAnOriginIsShown_NeverAPathOrQuery()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var acronis = (await service.GetAsync()).Runtime!.Acronis;

        var datacenter =
            Assert.Single(acronis.Details, d => d.Label == "Datacenter");

        Assert.Equal("https://example.acronis.invalid", datacenter.Value);
    }

    [Fact]
    public async Task ZendeskTicketing_ShowsDestinationAndIds_ButNoSecret()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var zendesk = (await service.GetAsync()).Runtime!.ZendeskTicketing;

        Assert.Equal("zd", zendesk.Alias);
        Assert.Contains(zendesk.Details, d => d.Label == "Instance" && d.Value == "https://example.zendesk.com");
        Assert.Contains(zendesk.Details, d => d.Label == "Destination" && d.Value == "dev");
        Assert.Contains(zendesk.Details, d => d.Label == "Group ID" && d.Value == "900000000003");
        Assert.Contains(zendesk.Details, d => d.Label == "Submitter user ID" && d.Value == "900000000001");
    }

    [Fact]
    public async Task NoSecret_AnywhereInTheStatus_NotEvenSerialised()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var json = JsonSerializer.Serialize(await service.GetAsync());

        Assert.DoesNotContain(AcronisSecret, json);
        Assert.DoesNotContain(ZendeskSecret, json);
        Assert.DoesNotContain("ClientSecret", json);
    }

    [Fact]
    public async Task SavedConnections_ShowOnlyWhetherASecretIsPresent()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var saved = (await service.GetAsync()).SavedConnections;

        var acronis = saved.Single(c => c.Kind == "Acronis");

        Assert.Contains(acronis.Details, d => d.Label == "Client secret" && d.Value == "configured");

        var deletion = saved.Single(c => c.Kind == "Zendesk (deletion)");

        Assert.Contains(deletion.Details, d => d.Value == "none (public client)");

        Assert.Equal(
            ["Acronis", "Business Central", "Zendesk (deletion)", "Zendesk (ticketing)"],
            saved.Select(c => c.Kind).Order().ToArray());
    }

    [Fact]
    public async Task AMissingSecret_IsInvalid_WithAReason_NotAnException()
    {
        var (service, _, _) = Create(FullConfiguration());

        var runtime = (await service.GetAsync()).Runtime!;

        Assert.Equal(ConnectionState.Invalid, runtime.Acronis.State);
        Assert.Contains("secret", runtime.Acronis.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(ConnectionState.Invalid, runtime.ZendeskTicketing.State);

        // The connections that need no secret are unaffected.
        Assert.Equal(ConnectionState.Valid, runtime.ZendeskDeletion.State);
        Assert.Equal(ConnectionState.Valid, runtime.BusinessCentral.State);
    }

    [Fact]
    public async Task ConnectionsTheProfileDoesNotUse_AreNotSelected()
    {
        var configuration = FullConfiguration();
        var profile = configuration.Profiles["work"];

        profile.ZendeskDeletionConnection = null;
        profile.BusinessCentralConnection = null;

        var (service, _, _) = Create(configuration, FullSecrets());

        var runtime = (await service.GetAsync()).Runtime!;

        Assert.Equal(ConnectionState.NotSelected, runtime.ZendeskDeletion.State);
        Assert.Equal(ConnectionState.NotSelected, runtime.BusinessCentral.State);
    }

    [Fact]
    public async Task ProductionWithoutAnAcronisConnection_SaysItIsRequired()
    {
        var configuration = FullConfiguration();
        configuration.Profiles["work"].AcronisConnection = null;

        var (service, _, _) = Create(configuration, FullSecrets());

        var acronis = (await service.GetAsync()).Runtime!.Acronis;

        Assert.Equal(ConnectionState.NotSelected, acronis.State);
        Assert.Contains("require an Acronis connection", acronis.Reason);
    }

    [Fact]
    public async Task FixtureMode_DoesNotDemandAnAcronisConnection()
    {
        var configuration = FullConfiguration(SupportToolkitMode.Fixture);
        configuration.Profiles["work"].AcronisConnection = null;

        var (service, _, _) = Create(configuration);

        var runtime = (await service.GetAsync()).Runtime!;

        Assert.Equal(SupportToolkitMode.Fixture, runtime.BackupAggregatorMode.Mode);
        Assert.Null(runtime.Acronis.Reason);
    }

    [Fact]
    public async Task AModeOverride_IsNamed_AndItsValueIsNotRevealed()
    {
        var (service, _, _) =
            Create(
                FullConfiguration(SupportToolkitMode.Production),
                FullSecrets(),
                new Dictionary<string, string?>
                {
                    ["SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"] = "fixture",
                    ["ACRONIS_CLIENT_SECRET"] = "ENV-SECRET-VALUE-77"
                });

        var status = await service.GetAsync();

        Assert.Equal(SupportToolkitMode.Fixture, status.Runtime!.BackupAggregatorMode.Mode);

        Assert.Equal(
            "SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE",
            status.Runtime.BackupAggregatorMode.OverriddenBy);

        Assert.Contains("ACRONIS_CLIENT_SECRET", status.EnvironmentOverridesSet);

        Assert.DoesNotContain(
            "ENV-SECRET-VALUE-77",
            JsonSerializer.Serialize(status));
    }

    [Fact]
    public async Task AnInvalidModeOverride_IsReported_NotThrown()
    {
        var (service, _, _) =
            Create(
                FullConfiguration(),
                FullSecrets(),
                new Dictionary<string, string?>
                {
                    ["SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"] = "banana"
                });

        var mode = (await service.GetAsync()).Runtime!.BackupAggregatorMode;

        Assert.Null(mode.Mode);
        Assert.NotEmpty(mode.Problem!);
    }

    [Fact]
    public async Task ObsoleteVariables_AreFlagged()
    {
        var (service, _, _) =
            Create(
                FullConfiguration(),
                FullSecrets(),
                new Dictionary<string, string?>
                {
                    ["SUPPORTTOOLKIT_ALLOW_WRITES"] = "true"
                });

        var status = await service.GetAsync();

        Assert.Contains("SUPPORTTOOLKIT_ALLOW_WRITES", status.ObsoleteEnvironmentVariablesSet);
        Assert.Empty(status.EnvironmentOverridesSet);
    }

    [Fact]
    public async Task Profiles_AreListedSorted_WithTheActiveOneMarked()
    {
        var (service, _, _) =
            Create(FullConfiguration(), FullSecrets());

        var profiles = (await service.GetAsync()).Profiles;

        Assert.Equal(["Spare", "work"], profiles.Select(p => p.Name).ToArray());
        Assert.False(profiles.Single(p => p.Name == "Spare").IsActive);
        Assert.True(profiles.Single(p => p.Name == "work").IsActive);
    }

    [Fact]
    public async Task SwitchingProfile_ChangesOnlyTheActiveProfile()
    {
        var configuration = FullConfiguration();
        var (service, store, _) = Create(configuration, FullSecrets());

        var result = await service.SetActiveProfileAsync("spare");

        Assert.True(result.Succeeded);

        // Matched case-insensitively, stored under its real name.
        Assert.Equal("Spare", store.Configuration.ActiveProfile);
        Assert.Equal(1, store.Saves);

        // Nothing else was touched.
        Assert.True(store.Configuration.Connections.Acronis.ContainsKey("acr"));
        Assert.Equal("acr", store.Configuration.Profiles["work"].AcronisConnection);
        Assert.Equal(
            SupportToolkitMode.Production,
            store.Configuration.Profiles["work"].Modules["backup-aggregator"]);

        var status = await service.GetAsync();

        Assert.Equal("Spare", status.ActiveProfile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("does-not-exist")]
    public async Task AnUnknownProfile_IsRefused_AndNothingIsSaved(string name)
    {
        var (service, store, _) = Create(FullConfiguration());

        var result = await service.SetActiveProfileAsync(name);

        Assert.False(result.Succeeded);
        Assert.Equal("work", store.Configuration.ActiveProfile);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task AnActiveProfileThatNoLongerExists_HasNoRuntime_ButStillListsProfiles()
    {
        var configuration = FullConfiguration();
        configuration.ActiveProfile = "gone";

        var (service, _, _) = Create(configuration, FullSecrets());

        var status = await service.GetAsync();

        Assert.True(status.HasProfiles);
        Assert.Null(status.ActiveProfile);
        Assert.Null(status.Runtime);
    }
}
