using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class BackupHealthRetirementTests
{
    [Fact]
    public async Task LegacyProfile_WithBackupHealth_LoadsAndResolvesWithoutError()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-retired-module-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            await File.WriteAllTextAsync(path, LegacyJson);

            var store = new JsonSupportToolkitConfigurationStore(path);
            var config = await store.LoadAsync();

            var profile = config.Profiles["trial"];

            Assert.Equal("example-acronis", profile.AcronisConnection);

            Assert.Equal(
                SupportToolkitMode.Production,
                profile.Modules["backup-aggregator"]);

            var resolver =
                new SupportToolkitConfigurationResolver(
                    store,
                    new NoSecrets());

            Assert.Equal(
                SupportToolkitMode.Production,
                await resolver.GetModuleModeAsync("backup-aggregator"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Save_DropsOnlyTheRetiredModuleEntry_AndKeepsTheConnection()
    {
        var directory = Directory.CreateTempSubdirectory("st-config-retired-module-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            await File.WriteAllTextAsync(path, LegacyJson);

            var store = new JsonSupportToolkitConfigurationStore(path);
            var config = await store.LoadAsync();

            await store.SaveAsync(config);
            var saved = await File.ReadAllTextAsync(path);

            Assert.DoesNotContain(
                "backup-health",
                saved,
                StringComparison.OrdinalIgnoreCase);

            // The shared Acronis connection and the live module stay.
            Assert.Contains("example-acronis", saved);
            Assert.Contains("https://example.invalid", saved);
            Assert.Contains("example-client", saved);
            Assert.Contains("backup-aggregator", saved);

            var reloaded = await store.LoadAsync();

            Assert.Equal(
                "example-acronis",
                reloaded.Profiles["trial"].AcronisConnection);

            Assert.True(
                reloaded.Connections.Acronis.ContainsKey("example-acronis"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private const string LegacyJson = """
        {
          "activeProfile": "trial",
          "connections": {
            "acronis": {
              "example-acronis": {
                "datacenterUrl": "https://example.invalid",
                "clientId": "example-client"
              }
            }
          },
          "profiles": {
            "trial": {
              "acronisConnection": "example-acronis",
              "modules": {
                "backup-aggregator": "production",
                "backup-health": "production"
              }
            }
          }
        }
        """;

    private sealed class NoSecrets : SupportToolkit.Core.Secrets.ISecretStore
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
