using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// The app has two Zendesk connections separated by function: ticketing
/// (client credentials, secret, destination) and deletion (public client,
/// interactive sign-in, no secret). They must resolve independently.
/// </summary>
public class ZendeskTwoConnectionsTests
{
    [Fact]
    public async Task Deletion_ResolvesWithoutASecret_AndNeverTouchesTheSecretStore()
    {
        var resolver =
            CreateResolver(
                DeletionOnly(),
                new ThrowingSecretStore());

        var resolved =
            await resolver.GetZendeskDeletionConnectionAsync();

        Assert.Equal("example-deletion", resolved.ConnectionName);
        Assert.Equal("example", resolved.Subdomain);
        Assert.Equal("example-public-client", resolved.ClientId);
    }

    [Fact]
    public async Task Deletion_IgnoresTheTicketingEnvironmentOverrides()
    {
        var resolver =
            CreateResolver(
                DeletionOnly(),
                new ThrowingSecretStore(),
                new Dictionary<string, string?>
                {
                    ["ZENDESK_SUBDOMAIN"] = "other",
                    ["ZENDESK_CLIENT_ID"] = "other-client",
                    ["ZENDESK_CLIENT_SECRET"] = "other-secret"
                });

        var resolved =
            await resolver.GetZendeskDeletionConnectionAsync();

        Assert.Equal("example", resolved.Subdomain);
        Assert.Equal("example-public-client", resolved.ClientId);
    }

    [Fact]
    public async Task Deletion_WithoutAReference_FailsWithAClearMessage()
    {
        var configuration = DeletionOnly();
        configuration.Profiles["work"].ZendeskDeletionConnection = null;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateResolver(configuration)
                    .GetZendeskDeletionConnectionAsync());

        Assert.Contains("deletion connection", exception.Message);
    }

    [Fact]
    public async Task Deletion_ReferencingAMissingConnection_Fails()
    {
        var configuration = DeletionOnly();
        configuration.Connections.ZendeskDeletion.Clear();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateResolver(configuration)
                    .GetZendeskDeletionConnectionAsync());

        Assert.Contains("does not exist", exception.Message);
    }

    [Theory]
    [InlineData("", "example-public-client")]
    [InlineData("example", "")]
    public async Task Deletion_RequiresSubdomainAndClientId(
        string subdomain,
        string clientId)
    {
        var configuration = DeletionOnly();

        var connection =
            configuration.Connections.ZendeskDeletion["example-deletion"];

        connection.Subdomain = subdomain;
        connection.ClientId = clientId;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateResolver(configuration)
                .GetZendeskDeletionConnectionAsync());
    }

    [Fact]
    public async Task ProfileWithOnlyTicketing_ResolvesTicketing_ButNotDeletion()
    {
        var resolver =
            CreateResolver(
                TicketingOnly(),
                new FixedSecretStore("example-secret"));

        var ticketing = await resolver.GetZendeskConnectionAsync();

        Assert.Equal("example-ticketing-client", ticketing.ClientId);
        Assert.Equal("example-secret", ticketing.ClientSecret);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.GetZendeskDeletionConnectionAsync());
    }

    [Fact]
    public async Task ProfileWithOnlyDeletion_ResolvesDeletion_ButNotTicketing()
    {
        var resolver =
            CreateResolver(
                DeletionOnly(),
                new FixedSecretStore("example-secret"));

        await resolver.GetZendeskDeletionConnectionAsync();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetZendeskConnectionAsync());

        Assert.Contains("Zendesk connection", exception.Message);
    }

    [Fact]
    public async Task BothConnections_ShareASubdomain_ButNotCredentials()
    {
        var configuration = TicketingOnly();

        var deletion = DeletionOnly();

        configuration.Connections.ZendeskDeletion =
            deletion.Connections.ZendeskDeletion;

        configuration.Profiles["work"].ZendeskDeletionConnection =
            "example-deletion";

        var resolver =
            CreateResolver(
                configuration,
                new FixedSecretStore("example-secret"));

        var ticketing = await resolver.GetZendeskConnectionAsync();
        var removal = await resolver.GetZendeskDeletionConnectionAsync();

        Assert.Equal(ticketing.Subdomain, removal.Subdomain);

        // Different OAuth clients: nothing is shared between them.
        Assert.NotEqual(ticketing.ClientId, removal.ClientId);
        Assert.Equal(
            "example-public-client",
            removal.ClientId);
    }

    [Fact]
    public async Task Store_RoundTripsTheDeletionConnection_WithoutAnySecret()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-zd-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var store = new JsonSupportToolkitConfigurationStore(path);

            await store.SaveAsync(DeletionOnly());

            var saved = await File.ReadAllTextAsync(path);

            Assert.Contains("zendeskDeletion", saved);
            Assert.Contains("zendeskDeletionConnection", saved);
            Assert.Contains("example-public-client", saved);
            Assert.DoesNotContain(
                "secret",
                saved,
                StringComparison.OrdinalIgnoreCase);

            var loaded = await store.LoadAsync();

            Assert.Equal(
                "example-public-client",
                loaded.Connections.ZendeskDeletion["example-deletion"]
                    .ClientId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigurationWithoutADeletionSection_StillLoads()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-zd-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");

            await File.WriteAllTextAsync(path, """
                {
                  "activeProfile": "work",
                  "connections": {
                    "zendesk": {
                      "example-ticketing": {
                        "subdomain": "example",
                        "clientId": "example-ticketing-client",
                        "submitterId": 1,
                        "requesterId": 2,
                        "destinations": { "dev": { "groupId": 3 } }
                      }
                    }
                  },
                  "profiles": {
                    "work": { "zendeskConnection": "example-ticketing" }
                  }
                }
                """);

            var loaded =
                await new JsonSupportToolkitConfigurationStore(path)
                    .LoadAsync();

            Assert.Empty(loaded.Connections.ZendeskDeletion);
            Assert.Null(loaded.Profiles["work"].ZendeskDeletionConnection);
            Assert.Equal(
                "example-ticketing",
                loaded.Profiles["work"].ZendeskConnection);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SupportToolkitConfiguration DeletionOnly() =>
        new()
        {
            ActiveProfile = "work",
            Connections = new SupportToolkitConnections
            {
                ZendeskDeletion =
                {
                    ["example-deletion"] =
                        new ZendeskDeletionConnectionConfiguration
                        {
                            Subdomain = "example",
                            ClientId = "example-public-client"
                        }
                }
            },
            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        ZendeskDeletionConnection = "example-deletion"
                    }
            }
        };

    private static SupportToolkitConfiguration TicketingOnly() =>
        new()
        {
            ActiveProfile = "work",
            Connections = new SupportToolkitConnections
            {
                Zendesk =
                {
                    ["example-ticketing"] =
                        new ZendeskConnectionConfiguration
                        {
                            Subdomain = "example",
                            ClientId = "example-ticketing-client",
                            SubmitterId = 900000000001L,
                            RequesterId = 900000000002L,
                            Destinations =
                            {
                                ["dev"] =
                                    new ZendeskDestinationConfiguration
                                    {
                                        GroupId = 900000000003L
                                    }
                            }
                        }
                }
            },
            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        ZendeskConnection = "example-ticketing",
                        ZendeskDestination = "dev"
                    }
            }
        };

    private static SupportToolkitConfigurationResolver CreateResolver(
        SupportToolkitConfiguration configuration,
        ISecretStore? secrets = null,
        Dictionary<string, string?>? environment = null) =>
        new(
            new MemoryStore(configuration),
            secrets ?? new FixedSecretStore(null),
            variable =>
                environment is not null
                && environment.TryGetValue(variable, out var value)
                    ? value
                    : null);

    private sealed class MemoryStore : ISupportToolkitConfigurationStore
    {
        private readonly SupportToolkitConfiguration _configuration;

        public MemoryStore(SupportToolkitConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedSecretStore : ISecretStore
    {
        private readonly string? _secret;

        public FixedSecretStore(string? secret)
        {
            _secret = secret;
        }

        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_secret);

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

    /// <summary>
    /// Proves the deletion connection never reads a secret.
    /// </summary>
    private sealed class ThrowingSecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The deletion connection must not read secrets.");

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The deletion connection must not write secrets.");

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "The deletion connection must not remove secrets.");
    }
}
