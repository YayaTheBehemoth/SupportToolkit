using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Ui.Web;

/// <summary>
/// Fictional configuration and in-memory stores for the web UI tests.
/// </summary>
internal static class StatusTestSupport
{
    public const string AcronisSecret = "ACRONIS-SECRET-VALUE-1f3a";
    public const string ZendeskSecret = "ZENDESK-SECRET-VALUE-9b2c";

    public static SupportToolkitConfiguration FullConfiguration(
        SupportToolkitMode mode = SupportToolkitMode.Production)
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",
                Profiles =
                {
                    ["work"] =
                        new SupportToolkitProfile
                        {
                            AcronisConnection = "acr",
                            ZendeskConnection = "zd",
                            ZendeskDestination = "dev",
                            ZendeskDeletionConnection = "del",
                            BusinessCentralConnection = "bc",
                            Modules = { ["backup-aggregator"] = mode },
                            MonitoringScope =
                            {
                                ["backup-aggregator"] =
                                    new ModuleMonitoringScopeConfiguration
                                    {
                                        MonitoringItemNumber = "EXAMPLE-ITEM"
                                    }
                            }
                        },
                    ["Spare"] =
                        new SupportToolkitProfile
                        {
                            Modules =
                            {
                                ["backup-aggregator"] =
                                    SupportToolkitMode.Fixture
                            }
                        }
                }
            };

        configuration.Connections.Acronis["acr"] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl = "https://example.acronis.invalid/api/path?x=1",
                ClientId = "example-acronis-client"
            };

        configuration.Connections.Zendesk["zd"] =
            new ZendeskConnectionConfiguration
            {
                Subdomain = "example",
                ClientId = "example-zendesk-client",
                SubmitterId = 900000000001,
                RequesterId = 900000000002,
                Destinations =
                {
                    ["dev"] =
                        new ZendeskDestinationConfiguration
                        {
                            GroupId = 900000000003
                        }
                }
            };

        configuration.Connections.ZendeskDeletion["del"] =
            new ZendeskDeletionConnectionConfiguration
            {
                Subdomain = "example",
                ClientId = "example-public-client"
            };

        configuration.Connections.BusinessCentral["bc"] =
            new BusinessCentralConnectionConfiguration
            {
                TenantId = "00000000-0000-0000-0000-000000000001",
                ClientId = "00000000-0000-0000-0000-000000000002",
                Environment = "example-environment",
                CompanyId = "00000000-0000-0000-0000-000000000003",
                ApiPublisher = "example",
                ApiGroup = "subscriptions",
                ApiVersion = "v1.0"
            };

        return configuration;
    }

    public static MemorySecrets FullSecrets() =>
        new(
            new Dictionary<string, string>
            {
                [SupportToolkitSecretKeys.AcronisClientSecret("acr")] =
                    AcronisSecret,
                [SupportToolkitSecretKeys.ZendeskClientSecret("zd")] =
                    ZendeskSecret
            });

    public static (ConfigurationStatusService Service,
        MemoryStore Store,
        SupportToolkitConfigurationResolver Resolver)
        Create(
            SupportToolkitConfiguration configuration,
            MemorySecrets? secrets = null,
            Dictionary<string, string?>? environment = null)
    {
        var store = new MemoryStore(configuration);
        secrets ??= new MemorySecrets([]);

        string? Read(string name) =>
            environment is not null
            && environment.TryGetValue(name, out var value)
                ? value
                : null;

        var resolver =
            new SupportToolkitConfigurationResolver(store, secrets, Read);

        return (
            new ConfigurationStatusService(store, resolver, secrets, Read),
            store,
            resolver);
    }

    public sealed class MemoryStore : ISupportToolkitConfigurationStore
    {
        public MemoryStore(SupportToolkitConfiguration configuration)
        {
            Configuration = configuration;
        }

        public SupportToolkitConfiguration Configuration { get; private set; }

        public int Saves { get; private set; }

        public string ConfigurationPath => "C:\\example\\local.config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            Configuration = configuration;
            Saves++;

            return Task.CompletedTask;
        }
    }

    public sealed class MemorySecrets : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets;

        public MemorySecrets(Dictionary<string, string> secrets)
        {
            _secrets = secrets;
        }

        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _secrets.TryGetValue(key, out var value) ? value : null);

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            _secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            _secrets.Remove(key);
            return Task.CompletedTask;
        }
    }
}
