using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Core.Configuration;

public class SupportToolkitConfigurationResolverTests
{
    [Fact]
    public async Task GetModuleModeAsync_UsesActiveProfile()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].Modules[
            "backup-aggregator"
        ] =
            SupportToolkitMode.Production;

        var resolver =
            CreateResolver(
                configuration
            );

        var mode =
            await resolver.GetModuleModeAsync(
                "backup-aggregator"
            );

        Assert.Equal(
            SupportToolkitMode.Production,
            mode
        );
    }

    [Fact]
    public async Task GetModuleModeAsync_EnvironmentOverridesProfile()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].Modules[
            "backup-aggregator"
        ] =
            SupportToolkitMode.Production;

        var resolver =
            CreateResolver(
                configuration,
                environment:
                    new Dictionary<string, string?>
                    {
                        ["SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"] =
                            "fixture"
                    }
            );

        var mode =
            await resolver.GetModuleModeAsync(
                "backup-aggregator"
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            mode
        );
    }

    [Fact]
    public async Task GetModuleModeAsync_WhenNoProfiles_DefaultsToFixture()
    {
        var resolver =
            CreateResolver(
                new SupportToolkitConfiguration()
                {
                    Profiles =
                        new Dictionary<
                            string,
                            SupportToolkitProfile>()
                }
            );

        var mode =
            await resolver.GetModuleModeAsync(
                "backup-health"
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            mode
        );
    }

    [Fact]
    public async Task GetAcronisConnectionAsync_UsesProfileConnectionAndSecretStore()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].AcronisConnection =
            "work-production";

        configuration.Connections.Acronis[
            "work-production"
        ] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl =
                    "https://fixture.acronis.invalid",

                ClientId =
                    "fixture-client"
            };

        var secrets =
            new Dictionary<string, string>
            {
                [
                    SupportToolkitSecretKeys
                        .AcronisClientSecret(
                            "work-production"
                        )
                ] =
                    "fixture-secret"
            };

        var resolver =
            CreateResolver(
                configuration,
                secrets:
                    secrets
            );

        var resolved =
            await resolver
                .GetAcronisConnectionAsync();

        Assert.Equal(
            "https://fixture.acronis.invalid",
            resolved.DatacenterUrl
        );

        Assert.Equal(
            "fixture-client",
            resolved.ClientId
        );

        Assert.Equal(
            "fixture-secret",
            resolved.ClientSecret
        );
    }

    [Fact]
    public async Task GetAcronisConnectionAsync_EnvironmentOverridesStoredConnection()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].AcronisConnection =
            "work-production";

        configuration.Connections.Acronis[
            "work-production"
        ] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl =
                    "https://stored.acronis.invalid",

                ClientId =
                    "stored-client"
            };

        var secrets =
            new Dictionary<string, string>
            {
                [
                    SupportToolkitSecretKeys
                        .AcronisClientSecret(
                            "work-production"
                        )
                ] =
                    "stored-secret"
            };

        var environment =
            new Dictionary<string, string?>
            {
                ["ACRONIS_DATACENTER_URL"] =
                    "https://override.acronis.invalid",

                ["ACRONIS_CLIENT_ID"] =
                    "override-client",

                ["ACRONIS_CLIENT_SECRET"] =
                    "override-secret"
            };

        var resolver =
            CreateResolver(
                configuration,
                secrets,
                environment
            );

        var resolved =
            await resolver
                .GetAcronisConnectionAsync();

        Assert.Equal(
            "https://override.acronis.invalid",
            resolved.DatacenterUrl
        );

        Assert.Equal(
            "override-client",
            resolved.ClientId
        );

        Assert.Equal(
            "override-secret",
            resolved.ClientSecret
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_UsesProfileConnectionAndSecretStore()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].ZendeskConnection =
            "sandbox";

        configuration.Connections.Zendesk[
            "sandbox"
        ] =
            new ZendeskConnectionConfiguration
            {
                Subdomain =
                    "fixture",

                ClientId =
                    "fixture-client"
            };

        var secrets =
            new Dictionary<string, string>
            {
                [
                    SupportToolkitSecretKeys
                        .ZendeskClientSecret(
                            "sandbox"
                        )
                ] =
                    "fixture-secret"
            };

        var resolver =
            CreateResolver(
                configuration,
                secrets:
                    secrets
            );

        var resolved =
            await resolver
                .GetZendeskConnectionAsync();

        Assert.Equal(
            "fixture",
            resolved.Subdomain
        );

        Assert.Equal(
            "fixture-client",
            resolved.ClientId
        );

        Assert.Equal(
            "fixture-secret",
            resolved.ClientSecret
        );
    }

    [Fact]
    public async Task GetZendeskConnectionAsync_WhenSecretMissing_ThrowsWithoutExposingSecret()
    {
        var configuration =
            CreateConfiguration();

        configuration.Profiles[
            "work"
        ].ZendeskConnection =
            "sandbox";

        configuration.Connections.Zendesk[
            "sandbox"
        ] =
            new ZendeskConnectionConfiguration
            {
                Subdomain =
                    "fixture",

                ClientId =
                    "fixture-client"
            };

        var resolver =
            CreateResolver(
                configuration
            );

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    resolver
                        .GetZendeskConnectionAsync()
            );

        Assert.Contains(
            "sandbox",
            exception.Message
        );

        Assert.Contains(
            "client secret",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static SupportToolkitConfiguration
        CreateConfiguration()
    {
        return new SupportToolkitConfiguration
        {
            ActiveProfile =
                "work",

            Profiles =
                new Dictionary<
                    string,
                    SupportToolkitProfile>
                {
                    ["work"] =
                        new()
                }
        };
    }

    private static SupportToolkitConfigurationResolver
        CreateResolver(
            SupportToolkitConfiguration configuration,
            Dictionary<string, string>? secrets = null,
            Dictionary<string, string?>? environment = null)
    {
        return new SupportToolkitConfigurationResolver(
            new TestConfigurationStore(
                configuration
            ),
            new TestSecretStore(
                secrets
                ?? new Dictionary<string, string>()
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
        private SupportToolkitConfiguration
            _configuration;

        public string ConfigurationPath =>
            "test-config.json";

        public TestConfigurationStore(
            SupportToolkitConfiguration configuration)
        {
            _configuration =
                configuration;
        }

        public Task<SupportToolkitConfiguration>
            LoadAsync(
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _configuration
            );
        }

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _configuration =
                configuration;

            return Task.CompletedTask;
        }
    }

    private sealed class TestSecretStore
        : ISecretStore
    {
        private readonly Dictionary<string, string>
            _secrets;

        public TestSecretStore(
            Dictionary<string, string> secrets)
        {
            _secrets =
                secrets;
        }

        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                _secrets.TryGetValue(
                    key,
                    out var value
                )
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

            _secrets[
                key
            ] =
                value;

            return Task.CompletedTask;
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _secrets.Remove(
                key
            );

            return Task.CompletedTask;
        }
    }
}