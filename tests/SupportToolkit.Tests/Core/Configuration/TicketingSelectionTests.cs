using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Providers.OsTicket.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// A profile selects one ticketing system. The Ticketing block wins; the
/// older Zendesk fields still select Zendesk on their own. All values are
/// fictional.
/// </summary>
public class TicketingSelectionTests
{
    private const string ApiKey = "EXAMPLE-OSTICKET-KEY-4c2d";

    private static SupportToolkitConfiguration OsTicketConfiguration()
    {
        var configuration = FullConfiguration();

        OsTicketConnections.Write(
            configuration,
            new Dictionary<string, OsTicketConnectionConfiguration>
            {
                ["lab"] =
                    new()
                    {
                        BaseUrl = "https://helpdesk.example.com/support/",
                        RequesterName = "Example Requester",
                        RequesterEmail = "requester@example.com",
                        Destinations =
                        {
                            ["backups"] = new OsTicketDestinationConfiguration { TopicId = 10 }
                        }
                    }
            });

        // The Zendesk fields stay set: the block must win over them.
        configuration.Profiles["work"].Ticketing =
            new TicketingSelectionConfiguration
            {
                Provider = "osTicket",
                Connection = "lab",
                Destination = "backups"
            };

        return configuration;
    }

    private static MemorySecrets OsTicketSecrets() =>
        new(new Dictionary<string, string>
        {
            [OsTicketConnections.ApiKey("lab").Key] = ApiKey,
            [SupportToolkitSecretKeys.ZendeskClientSecret("zd")] = ZendeskSecret
        });

    /// <summary>Edits the "lab" osTicket connection in place.</summary>
    private static void Lab(
        SupportToolkitConfiguration configuration,
        Action<OsTicketConnectionConfiguration> edit)
    {
        var connections =
            new Dictionary<string, OsTicketConnectionConfiguration>(
                OsTicketConnections.Read(configuration));

        edit(connections["lab"]);

        OsTicketConnections.Write(configuration, connections);
    }

    // ---------------------------------------------------------------
    // Reading the selection
    // ---------------------------------------------------------------

    [Fact]
    public void TheOlderZendeskFields_StillSelectZendesk()
    {
        var selection = TicketingSelection.For(FullConfiguration().Profiles["work"]);

        Assert.Equal(new TicketingSelection("zendesk", "zd", "dev"), selection);
        Assert.Equal("Zendesk", TicketingComposition.SystemName(selection!));
    }

    [Fact]
    public void TheTicketingBlock_WinsOverTheZendeskFields()
    {
        var selection = TicketingSelection.For(OsTicketConfiguration().Profiles["work"]);

        Assert.Equal(new TicketingSelection("osticket", "lab", "backups"), selection);
        Assert.Equal("osTicket", TicketingComposition.SystemName(selection!));
    }

    [Fact]
    public void AProfileWithNeither_SendsTicketsNowhere()
    {
        Assert.Null(TicketingSelection.For(new SupportToolkitProfile()));
    }

    [Fact]
    public void AZendeskBlock_MovesIntoTheZendeskFields_ForTheWizard()
    {
        var profile =
            new SupportToolkitProfile
            {
                Ticketing = new() { Provider = "zendesk", Connection = "zd", Destination = "dev" }
            };

        Assert.Null(TicketingSelection.MoveZendeskBlockToZendeskFields(profile));

        Assert.Null(profile.Ticketing);
        Assert.Equal("zd", profile.ZendeskConnection);
        Assert.Equal("dev", profile.ZendeskDestination);
    }

    [Fact]
    public void AnotherSystemsBlock_IsLeftAlone_ForTheWizard()
    {
        var profile = OsTicketConfiguration().Profiles["work"];

        var other = TicketingSelection.MoveZendeskBlockToZendeskFields(profile);

        Assert.Equal("osticket", other!.Provider);
        Assert.NotNull(profile.Ticketing);
    }

    [Fact]
    public async Task TheFile_KeepsTheBlockAndTheConnection_InTheDocumentedShape()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-osticket-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var store = new JsonSupportToolkitConfigurationStore(path);

            await store.SaveAsync(OsTicketConfiguration());

            var saved = await File.ReadAllTextAsync(path);

            Assert.Contains("\"ticketing\"", saved);
            Assert.Contains("\"osTicket\"", saved);
            Assert.Contains("\"topicId\": 10", saved);
            Assert.Contains("\"requesterEmail\"", saved);
            Assert.DoesNotContain(ApiKey, saved);

            var loaded = await store.LoadAsync();

            Assert.Equal(
                new TicketingSelection("osticket", "lab", "backups"),
                TicketingSelection.For(loaded.Profiles["work"]));
            Assert.Equal(
                10,
                OsTicketConnections.Read(loaded)["lab"].Destinations["backups"].TopicId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    // ---------------------------------------------------------------
    // Resolution
    // ---------------------------------------------------------------

    [Fact]
    public async Task AZendeskBlock_ResolvesLikeTheOlderFields()
    {
        var configuration = FullConfiguration();
        var profile = configuration.Profiles["work"];
        profile.ZendeskConnection = null;
        profile.ZendeskDestination = null;
        profile.Ticketing = new() { Provider = "zendesk", Connection = "zd", Destination = "dev" };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var target = await TicketingComposition.ResolveTargetAsync(resolver);

        Assert.Equal("Zendesk", target.Destination.SystemName);
        Assert.Equal("900000000003", target.Destination.RoutingId);
    }

    [Fact]
    public async Task AnOsTicketProfile_ResolvesItsHelpdeskAndTopic_WithItsLimits()
    {
        var (_, _, resolver) = Create(OsTicketConfiguration(), OsTicketSecrets());

        var target = await TicketingComposition.ResolveTargetAsync(resolver);

        Assert.Equal(
            new TicketDestinationPreview(
                "osTicket", "helpdesk.example.com", "backups", "Help topic ID", "10"),
            target.Destination with { Caveat = null });
        Assert.Contains("default department", target.Destination.Caveat);
        Assert.Contains("retry creates another ticket", target.Destination.Caveat);
        Assert.Equal(
            "osticket:https://helpdesk.example.com/support/:10",
            target.IdempotencyIdentity);
        Assert.DoesNotContain(target.Details, detail => detail.Value.Contains(ApiKey));
    }

    [Fact]
    public async Task ZendeskResolution_RefusesAProfileThatSendsElsewhere()
    {
        var (_, _, resolver) = Create(OsTicketConfiguration(), OsTicketSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetZendeskConnectionAsync());

        Assert.Contains("'osticket', not Zendesk", error.Message);
    }

    public static TheoryData<Action<SupportToolkitConfiguration>, string> BrokenOsTicket() =>
        new()
        {
            { c => c.Profiles["work"].Ticketing!.Connection = null, "names no connection" },
            { c => c.Profiles["work"].Ticketing!.Connection = "missing", "does not exist" },
            { c => c.Profiles["work"].Ticketing!.Destination = null, "does not select" },
            { c => c.Profiles["work"].Ticketing!.Destination = "missing", "does not exist on connection" },
            { c => Lab(c, lab => lab.Destinations["backups"].TopicId = 0), "positive topicId" },
            { c => Lab(c, lab => lab.BaseUrl = ""), "baseUrl" },
            { c => Lab(c, lab => lab.BaseUrl = "http://helpdesk.example.com/"), "HTTPS" },
            { c => Lab(c, lab => lab.RequesterEmail = ""), "requesterEmail" },
            { c => Lab(c, lab => lab.RequesterEmail = "nobody"), "requesterEmail" },
            { c => c.Profiles["work"].Ticketing!.Provider = "", "names no provider" },
            { c => c.Profiles["work"].Ticketing!.Provider = "jira", "'jira'" }
        };

    [Theory]
    [MemberData(nameof(BrokenOsTicket))]
    public async Task AnIncompleteOsTicketSelection_FailsLocally_AndSaysWhy(
        Action<SupportToolkitConfiguration> breakIt,
        string expected)
    {
        var configuration = OsTicketConfiguration();
        breakIt(configuration);

        var (_, _, resolver) = Create(configuration, OsTicketSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketingComposition.ResolveTargetAsync(resolver));

        Assert.Contains(expected, error.Message);
        Assert.DoesNotContain(ApiKey, error.Message);
    }

    [Fact]
    public async Task AMissingKey_NamesItsVariable_WhereSecretsComeFromTheEnvironment()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(OsTicketConfiguration()),
                new EnvironmentSecretStore(_ => null),
                _ => null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketingComposition.ResolveTargetAsync(resolver));

        Assert.Contains(
            "Set SUPPORTTOOLKIT_SECRET__OSTICKET__LAB__API_KEY.",
            error.Message);
    }

    [Fact]
    public async Task NoSelection_IsReportedAsNoTicketDestination()
    {
        var configuration = FullConfiguration();
        configuration.Profiles["work"].ZendeskConnection = null;

        var (_, _, resolver) = Create(configuration, FullSecrets());

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketingComposition.ResolveTargetAsync(resolver));

        Assert.Contains("no ticket destination", error.Message);
    }

    // ---------------------------------------------------------------
    // Secrets and status
    // ---------------------------------------------------------------

    [Fact]
    public async Task TheOsTicketKey_IsARequiredSecret_OnlyWhileAProfileUsesIt()
    {
        var configuration = OsTicketConfiguration();

        var missing =
            await RequiredSecrets.FindMissingAsync(
                configuration, FullSecrets(), ProviderConnectionsComposition.All);

        var item = Assert.Single(missing, m => m.Provider == "osTicket");
        Assert.Equal("lab", item.ConnectionName);
        Assert.Equal("API key", item.SecretName);

        // The block wins, so the Zendesk fields no longer need a secret.
        Assert.DoesNotContain(missing, m => m.Provider.StartsWith("Zendesk"));

        configuration.Profiles["work"].Ticketing = null;

        Assert.DoesNotContain(
            await RequiredSecrets.FindMissingAsync(
                configuration, FullSecrets(), ProviderConnectionsComposition.All),
            m => m.Provider == "osTicket");
    }

    [Fact]
    public void OsTicketConnections_ThatShareAVariable_AreAClash()
    {
        var configuration = OsTicketConfiguration();
        OsTicketConnections.Write(
            configuration,
            new Dictionary<string, OsTicketConnectionConfiguration>(
                OsTicketConnections.Read(configuration))
            {
                ["lab_eu"] = new(),
                ["lab-eu"] = new()
            });

        var clash =
            Assert.Single(
                SecretLocationClashes.Find(
                    configuration,
                    new EnvironmentSecretStore(_ => null),
                    ProviderConnectionsComposition.All));

        Assert.Equal("osTicket", clash.Provider);
    }

    [Fact]
    public async Task AClashingKey_IsRefused_NeverSentForTheOtherConnection()
    {
        var configuration = OsTicketConfiguration();

        Lab(configuration, _ => { });
        var connections =
            new Dictionary<string, OsTicketConnectionConfiguration>(
                OsTicketConnections.Read(configuration));
        connections["lab-eu"] = connections["lab"];
        connections["lab_eu"] = new();
        OsTicketConnections.Write(configuration, connections);
        configuration.Profiles["work"].Ticketing!.Connection = "lab-eu";

        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(configuration),
                new EnvironmentSecretStore(name =>
                    name == "SUPPORTTOOLKIT_SECRET__OSTICKET__LAB_EU__API_KEY" ? ApiKey : null),
                _ => null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketingComposition.ResolveTargetAsync(resolver));

        Assert.Contains("'lab-eu' and 'lab_eu'", error.Message);
        Assert.Contains("API key", error.Message);
        Assert.DoesNotContain(ApiKey, error.Message);
    }

    [Fact]
    public async Task Status_ShowsTheOsTicketDestination_AndItsLimits_ButNoKey()
    {
        var (service, _, _) = Create(OsTicketConfiguration(), OsTicketSecrets());

        var status = await service.GetAsync();
        var ticketing = status.Runtime!.Ticketing;

        Assert.Equal("osTicket ticket submission", ticketing.Name);
        Assert.Equal(ConnectionState.Valid, ticketing.State);
        Assert.Equal("lab", ticketing.Alias);
        Assert.Contains(ticketing.Details, d => d.Label == "Help topic ID" && d.Value == "10");
        Assert.Contains(ticketing.Details, d => d.Label == "Helpdesk" && d.Value == "https://helpdesk.example.com/support/");
        Assert.Contains("default department", ticketing.Reason);
        Assert.Equal(
            new TicketingSelection("osticket", "lab", "backups"),
            status.Profiles.Single(p => p.Name == "work").Ticketing);
        Assert.DoesNotContain(ApiKey, System.Text.Json.JsonSerializer.Serialize(status));
    }

    [Fact]
    public async Task Status_OfAnUnknownSystem_IsInvalid_AndNamesIt()
    {
        var configuration = OsTicketConfiguration();
        configuration.Profiles["work"].Ticketing!.Provider = "jira";

        var (service, _, _) = Create(configuration, OsTicketSecrets());

        var ticketing = (await service.GetAsync()).Runtime!.Ticketing;

        Assert.Equal("jira ticket submission", ticketing.Name);
        Assert.Equal(ConnectionState.Invalid, ticketing.State);
        Assert.Contains("does not know", ticketing.Reason);
    }

    // ---------------------------------------------------------------
    // The sink is the same for every system
    // ---------------------------------------------------------------

    [Fact]
    public async Task TheSink_SubmitsThroughTheResolvedTarget_WithItsIdentity()
    {
        var target = new FakeTarget("10");
        var sink = new TicketingTargetSink(_ => Task.FromResult<ITicketingTarget>(target));

        var draft = new TicketDraft { Subject = "Subject", Body = "Body" };

        var ticket = await sink.SubmitAsync(draft, target.Destination, CancellationToken.None);

        Assert.Equal("T-1", ticket.Id);
        Assert.StartsWith("supporttoolkit-backup-review-", target.Provider.IdempotencyKey);
        Assert.True(target.SessionDisposed);
    }

    [Fact]
    public async Task TheSink_RefusesADestinationThatChangedSinceConfirmation()
    {
        var target = new FakeTarget("11");
        var sink = new TicketingTargetSink(_ => Task.FromResult<ITicketingTarget>(target));

        var confirmed = new FakeTarget("10").Destination;

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sink.SubmitAsync(
                    new TicketDraft { Subject = "Subject", Body = "Body" },
                    confirmed,
                    CancellationToken.None));

        Assert.Contains("osTicket destination changed since confirmation", error.Message);
        Assert.Null(target.Provider.IdempotencyKey);
    }

    private sealed class FakeTarget(string topicId) : ITicketingTarget
    {
        public FakeProvider Provider { get; } = new();

        public bool SessionDisposed { get; private set; }

        public TicketDestinationPreview Destination { get; } =
            new("osTicket", "helpdesk.example.com", "backups", "Help topic ID", topicId);

        public IReadOnlyList<StatusDetail> Details => [];

        public string IdempotencyIdentity => "osticket:example:" + topicId;

        public ITicketingSession OpenSession(OperationalLogger logger) =>
            new Session(this);

        private sealed class Session(FakeTarget owner) : ITicketingSession
        {
            public ITicketProvider TicketProvider => owner.Provider;

            public void Dispose() => owner.SessionDisposed = true;
        }
    }

    private sealed class FakeProvider : ITicketProvider
    {
        public string? IdempotencyKey { get; private set; }

        public Task<CreatedTicket> CreateTicketAsync(
            TicketDraft draft,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            IdempotencyKey = idempotencyKey;
            return Task.FromResult(new CreatedTicket("T-1", draft.Subject, null));
        }
    }
}
