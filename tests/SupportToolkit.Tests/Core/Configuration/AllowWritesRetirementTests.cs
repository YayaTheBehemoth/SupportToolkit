using SupportToolkit.Core.Configuration;

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

}
