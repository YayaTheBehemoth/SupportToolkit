using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Tests.Core.Configuration;

public class AllowWritesRetirementTests
{
    [Fact]
    public async Task LegacyProfile_LoadAndSave_DropsObsoleteAllowWritesProperty()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-migration-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            await File.WriteAllTextAsync(path, """
                {
                  "activeProfile": "trial",
                  "profiles": {
                    "trial": {
                      "allowWrites": false,
                      "zendeskConnection": "trial-zendesk",
                      "zendeskDestination": "dev",
                      "modules": { "backup-aggregator": "fixture" }
                    }
                  }
                }
                """);

            var store = new JsonSupportToolkitConfigurationStore(path);
            var config = await store.LoadAsync();
            var profile = config.Profiles["trial"];

            Assert.Equal("trial-zendesk", profile.ZendeskConnection);
            Assert.Equal("dev", profile.ZendeskDestination);
            Assert.Equal(
                SupportToolkitMode.Fixture,
                profile.Modules["backup-aggregator"]);

            await store.SaveAsync(config);
            var saved = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain(
                "allowWrites",
                saved,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("trial-zendesk", saved);
            Assert.Contains("\"dev\"", saved);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RetiredPermission_IsNotPartOfProfileOrResolverContract()
    {
        Assert.Null(typeof(SupportToolkitProfile).GetProperty("AllowWrites"));
        Assert.Null(typeof(SupportToolkitConfigurationResolver).GetMethod("GetAllowWritesAsync"));
    }

    [Fact]
    public async Task LegacyEnvironmentFlagDoesNotBlockDestinationValidation()
    {
        var configuration = new SupportToolkitConfiguration
        {
            ActiveProfile = "trial",
            Profiles = new Dictionary<string, SupportToolkitProfile>
            {
                ["trial"] = new()
                {
                    Modules = new Dictionary<string, SupportToolkitMode>
                    {
                        ["backup-aggregator"] = SupportToolkitMode.Fixture
                    }
                }
            }
        };

        var resolver = new SupportToolkitConfigurationResolver(
            new StubConfigStore(configuration),
            new EmptySecretStore(),
            variable => variable == "SUPPORTTOOLKIT_ALLOW_WRITES"
                ? "false"
                : null);

        var workflow = new BackupAggregatorWorkflow(resolver);
        var fixtureReport = new BackupAggregatorFixtureFactory().BuildReport();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.SubmitTicketAsync(fixtureReport));

        Assert.Contains("Zendesk connection", exception.Message);
        Assert.DoesNotContain(
            "External writes are disabled",
            exception.Message);
    }

    private sealed class StubConfigStore : ISupportToolkitConfigurationStore
    {
        private readonly SupportToolkitConfiguration _configuration;

        public StubConfigStore(SupportToolkitConfiguration configuration) =>
            _configuration = configuration;

        public string ConfigurationPath => "synthetic.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

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
}
