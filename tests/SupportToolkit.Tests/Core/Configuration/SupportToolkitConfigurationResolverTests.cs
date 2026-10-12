
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Core.Configuration;

public class SupportToolkitConfigurationResolverTests
{
    [Fact]
    public async Task GetModuleModeAsync_UsesActiveProfile()
    {
        var configuration = CreateConfiguration();

        configuration.Profiles["work"].Modules["backup-aggregator"] =
            SupportToolkitMode.Production;

        var resolver = CreateResolver(configuration);

        var mode = await resolver.GetModuleModeAsync(
            "backup-aggregator"
        );

        Assert.Equal(SupportToolkitMode.Production, mode);
    }

    [Fact]
    public async Task GetModuleModeAsync_EnvironmentOverridesProfile()
    {
        var configuration = CreateConfiguration();

        configuration.Profiles["work"].Modules["backup-aggregator"] =
            SupportToolkitMode.Production;

        var resolver = CreateResolver(
            configuration,
            environment: new Dictionary<string, string?>
            {
                ["SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"] =
                    "fixture"
            }
        );

        var mode = await resolver.GetModuleModeAsync(
            "backup-aggregator"
        );

        Assert.Equal(SupportToolkitMode.Fixture, mode);
    }

    [Fact]
    public async Task GetModuleModeAsync_WhenNoProfiles_DefaultsToFixture()
    {
        var configuration = new SupportToolkitConfiguration
        {
            Profiles = new()
        };

        var resolver = CreateResolver(configuration);

        var mode = await resolver.GetModuleModeAsync(
            "backup-health"
        );

        Assert.Equal(SupportToolkitMode.Fixture, mode);
    }

    [Fact]
    public async Task GetAcronisConnectionAsync_UsesProfileConnectionAndSecretStore()
    {
        var configuration = CreateConfiguration();

        configuration.Profiles["work"].AcronisConnection =
            "work-production";

        configuration.Connections.Acronis["work-production"] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl = "https://fixture.acronis.invalid",
                ClientId = "fixture-client"
            };

        var secrets = new Dictionary<string, string>
        {
            [SupportToolkitSecretKeys.AcronisClientSecret(
                "work-production")] = "fixture-secret"
        };

        var resolver = CreateResolver(configuration, secrets);

        var resolved = await resolver.GetAcronisConnectionAsync();

        Assert.Equal(
            "https://fixture.acronis.invalid",
            resolved.DatacenterUrl
        );

        Assert.Equal("fixture-client", resolved.ClientId);
        Assert.Equal("fixture-secret", resolved.ClientSecret);
    }

    [Fact]
    public async Task GetAcronisConnectionAsync_EnvironmentOverridesStoredConnection()
    {
        var configuration = CreateConfiguration();

        configuration.Profiles["work"].AcronisConnection =
            "work-production";

        configuration.Connections.Acronis["work-production"] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl = "https://stored.acronis.invalid",
                ClientId = "stored-client"
            };

        var secrets = new Dictionary<string, string>
        {
            [SupportToolkitSecretKeys.AcronisClientSecret(
                "work-production")] = "stored-secret"
        };

        var environment = new Dictionary<string, string?>
        {
            ["ACRONIS_DATACENTER_URL"] =
                "https://override.acronis.invalid",
            ["ACRONIS_CLIENT_ID"] = "override-client",
            ["ACRONIS_CLIENT_SECRET"] = "override-secret"
        };

        var resolver = CreateResolver(
            configuration,
            secrets,
            environment
        );

        var resolved = await resolver.GetAcronisConnectionAsync();

        Assert.Equal(
            "https://override.acronis.invalid",
            resolved.DatacenterUrl
        );

        Assert.Equal("override-client", resolved.ClientId);
        Assert.Equal("override-secret", resolved.ClientSecret);
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_ResolvesSelectedDestination()
    {
        var configuration = CreateZendeskConfiguration();

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var resolved = await resolver.GetZendeskConnectionAsync();

        Assert.Equal("fixture", resolved.Subdomain);
        Assert.Equal("fixture-client", resolved.ClientId);
        Assert.Equal("fixture-secret", resolved.ClientSecret);
        Assert.Equal("dev", resolved.DestinationName);
        Assert.Equal(111111L, resolved.GroupId);
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_ResolvesOperationsDestination()
    {
        var configuration = CreateZendeskConfiguration();

        configuration.Profiles["work"].ZendeskDestination =
            "operations";

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var resolved = await resolver.GetZendeskConnectionAsync();

        Assert.Equal("operations", resolved.DestinationName);
        Assert.Equal(222222L, resolved.GroupId);
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_DestinationLookupIgnoresCase()
    {
        var configuration = CreateZendeskConfiguration();

        configuration.Profiles["work"].ZendeskDestination =
            "DEV";

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var resolved = await resolver.GetZendeskConnectionAsync();

        Assert.Equal("dev", resolved.DestinationName);
        Assert.Equal(111111L, resolved.GroupId);
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_WithoutDestination_FailsClosed()
    {
        var configuration = CreateZendeskConfiguration();

        configuration.Profiles["work"].ZendeskDestination =
            null;

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );

        Assert.Contains(
            "destination",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_UnknownDestination_FailsClosed()
    {
        var configuration = CreateZendeskConfiguration();

        configuration.Profiles["work"].ZendeskDestination =
            "missing";

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );

        Assert.Contains("missing", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetZendeskConnectionAsync_InvalidGroupId_FailsClosed(
        long groupId)
    {
        var configuration = CreateZendeskConfiguration();

        configuration.Connections.Zendesk["sandbox"]
            .Destinations["dev"].GroupId = groupId;

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets()
        );

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );

        Assert.Contains(
            "group ID",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_DifferentEnvironmentInstance_FailsClosed()
    {
        var configuration = CreateZendeskConfiguration();

        var environment = new Dictionary<string, string?>
        {
            ["ZENDESK_SUBDOMAIN"] = "different-instance",
            ["ZENDESK_CLIENT_ID"] = "other-client",
            ["ZENDESK_CLIENT_SECRET"] = "other-secret"
        };

        var resolver = CreateResolver(
            configuration,
            ZendeskSecrets(),
            environment
        );

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );

        Assert.Contains(
            "ZENDESK_SUBDOMAIN",
            exception.Message
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_SameInstanceCredentialOverride_IsAllowed()
    {
        var configuration = CreateZendeskConfiguration();

        var environment = new Dictionary<string, string?>
        {
            ["ZENDESK_SUBDOMAIN"] = "FIXTURE",
            ["ZENDESK_CLIENT_ID"] = "override-client",
            ["ZENDESK_CLIENT_SECRET"] = "override-secret"
        };

        var resolver = CreateResolver(
            configuration,
            environment: environment
        );

        var resolved = await resolver.GetZendeskConnectionAsync();

        Assert.Equal("fixture", resolved.Subdomain);
        Assert.Equal("override-client", resolved.ClientId);
        Assert.Equal("override-secret", resolved.ClientSecret);
        Assert.Equal(111111L, resolved.GroupId);
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_EnvironmentCredentialsWithoutProfile_FailsClosed()
    {
        var configuration = new SupportToolkitConfiguration
        {
            Profiles = new()
        };

        var environment = new Dictionary<string, string?>
        {
            ["ZENDESK_SUBDOMAIN"] = "fixture",
            ["ZENDESK_CLIENT_ID"] = "fixture-client",
            ["ZENDESK_CLIENT_SECRET"] = "fixture-secret"
        };

        var resolver = CreateResolver(
            configuration,
            environment: environment
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_WhenSecretMissing_ThrowsWithoutExposingSecret()
    {
        var configuration = CreateZendeskConfiguration();

        var resolver = CreateResolver(configuration);

        var exception = await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => resolver.GetZendeskConnectionAsync()
        );

        Assert.Contains("sandbox", exception.Message);

        Assert.Contains(
            "client secret",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static SupportToolkitConfiguration CreateConfiguration()
    {
        return new SupportToolkitConfiguration
        {
            ActiveProfile = "work",

            Profiles = new Dictionary<
                string,
                SupportToolkitProfile>
            {
                ["work"] = new()
            }
        };
    }

    private static SupportToolkitConfiguration
        CreateZendeskConfiguration()
    {
        var configuration = CreateConfiguration();

        configuration.Profiles["work"].ZendeskConnection =
            "sandbox";

        configuration.Profiles["work"].ZendeskDestination =
            "dev";

        configuration.Connections.Zendesk["sandbox"] =
            new ZendeskConnectionConfiguration
            {
                Subdomain = "fixture",
                ClientId = "fixture-client",

                Destinations = new Dictionary<
                    string,
                    ZendeskDestinationConfiguration>
                {
                    ["dev"] = new()
                    {
                        GroupId = 111111
                    },

                    ["operations"] = new()
                    {
                        GroupId = 222222
                    }
                }
            };

        return configuration;
    }

    private static Dictionary<string, string> ZendeskSecrets()
    {
        return new Dictionary<string, string>
        {
            [SupportToolkitSecretKeys.ZendeskClientSecret(
                "sandbox")] = "fixture-secret"
        };
    }

    private static SupportToolkitConfigurationResolver
        CreateResolver(
            SupportToolkitConfiguration configuration,
            Dictionary<string, string>? secrets = null,
            Dictionary<string, string?>? environment = null)
    {
        return new SupportToolkitConfigurationResolver(
            new TestConfigurationStore(configuration),
            new TestSecretStore(
                secrets ?? new Dictionary<string, string>()
            ),
            variable =>
                environment is not null
                && environment.TryGetValue(
                    variable,
                    out var value
                )
                    ? value
                    : null
        );
    }

    private sealed class TestConfigurationStore
        : ISupportToolkitConfigurationStore
    {
        private SupportToolkitConfiguration _configuration;

        public string ConfigurationPath => "test-config.json";

        public TestConfigurationStore(
            SupportToolkitConfiguration configuration)
        {
            _configuration = configuration;
        }

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(_configuration);
        }

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _configuration = configuration;

            return Task.CompletedTask;
        }
    }

    private sealed class TestSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets;

        public TestSecretStore(Dictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _secrets.TryGetValue(key, out var value)
                    ? value
                    : null
            );
        }

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _secrets[key] = value;

            return Task.CompletedTask;
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _secrets.Remove(key);

            return Task.CompletedTask;
        }
    }
}
