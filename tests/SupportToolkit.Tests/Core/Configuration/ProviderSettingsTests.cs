using System.Text.Json;
using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.OsTicket.Configuration;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// A provider that owns its connection settings keeps them in its own
/// section, which Core reads and writes without knowing their shape.
/// </summary>
public class ProviderSettingsTests
{
    private const string Section = "exampleSystem";

    private sealed class ExampleConnection : ConfigurationNode
    {
        public string Url { get; set; } = string.Empty;
    }

    private static SupportToolkitConfiguration Load(string json)
    {
        var directory = Directory.CreateTempSubdirectory("st-provider-settings-");

        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            File.WriteAllText(path, json);
            return new JsonSupportToolkitConfigurationStore(path).LoadAsync().GetAwaiter().GetResult();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AHandWrittenSection_IsRead_WhateverItsCase()
    {
        var configuration =
            Load("""
                 {
                   "connections": {
                     "ExampleSYSTEM": { "Lab": { "url": "https://example.com/" } }
                   }
                 }
                 """);

        var connections =
            ProviderSettings.ReadConnections<ExampleConnection>(configuration, Section);

        Assert.Equal("https://example.com/", connections["lab"].Url);
    }

    [Fact]
    public void AMissingSection_IsNoConnections()
    {
        Assert.Empty(
            ProviderSettings.ReadConnections<ExampleConnection>(
                new SupportToolkitConfiguration(),
                Section));
    }

    [Fact]
    public void AMalformedSection_IsReported_NotIgnored()
    {
        var configuration =
            Load("""{ "connections": { "exampleSystem": ["not", "connections"] } }""");

        var error =
            Assert.Throws<InvalidOperationException>(
                () => ProviderSettings.ReadConnections<ExampleConnection>(configuration, Section));

        Assert.Contains("connections.exampleSystem", error.Message);
    }

    [Fact]
    public async Task AWrittenSection_SurvivesSaveAndLoad_AndKeepsSettingsItDoesNotKnow()
    {
        var configuration =
            Load("""
                 {
                   "connections": {
                     "exampleSystem": { "lab": { "url": "https://example.com/", "future": 1 } }
                   }
                 }
                 """);

        var connections =
            new Dictionary<string, ExampleConnection>(
                ProviderSettings.ReadConnections<ExampleConnection>(configuration, Section));

        connections["lab"].Url = "https://example.org/";
        connections["other"] = new ExampleConnection { Url = "https://example.net/" };

        ProviderSettings.WriteConnections(configuration, Section, connections);

        var directory = Directory.CreateTempSubdirectory("st-provider-settings-");

        try
        {
            var store =
                new JsonSupportToolkitConfigurationStore(
                    Path.Combine(directory.FullName, "config.json"));

            await store.SaveAsync(configuration);

            var saved = await File.ReadAllTextAsync(store.ConfigurationPath);
            Assert.Contains("\"future\": 1", saved);
            Assert.Single(
                JsonDocument.Parse(saved).RootElement.GetProperty("connections").EnumerateObject(),
                property => property.Name.Equals(Section, StringComparison.OrdinalIgnoreCase));

            var reloaded =
                ProviderSettings.ReadConnections<ExampleConnection>(
                    await store.LoadAsync(),
                    Section);

            Assert.Equal("https://example.org/", reloaded["lab"].Url);
            Assert.Equal("https://example.net/", reloaded["other"].Url);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ASecretKey_OfAProviderOwningItsSettings_IsNormalizedLikeTheOthers()
    {
        Assert.Equal(
            "osticket:lab-eu:api-key",
            SupportToolkitSecretKeys.For("osTicket", " LAB-EU ", "API-Key"));
    }

    /// <summary>
    /// An unregistered provider would be skipped by the required-secret and
    /// clash checks without any error, so every one must be registered.
    /// </summary>
    [Fact]
    public void EveryProviderThatOwnsItsSettings_IsRegistered()
    {
        var implementations =
            typeof(OsTicketConnections).Assembly.GetTypes()
                .Where(type =>
                    typeof(IProviderConnections).IsAssignableFrom(type)
                    && type is { IsInterface: false, IsAbstract: false })
                .ToArray();

        Assert.NotEmpty(implementations);

        foreach (var type in implementations)
        {
            Assert.Contains(
                ProviderConnectionsComposition.All,
                registered => registered.GetType() == type);
        }
    }

    [Fact]
    public async Task Status_ListsAProviderOwnedConnection_WithItsSecretState_ButNoValue()
    {
        var configuration = FullConfiguration();

        OsTicketConnections.Write(
            configuration,
            new Dictionary<string, OsTicketConnectionConfiguration>
            {
                ["lab"] = new() { BaseUrl = "https://helpdesk.example.com/" }
            });

        var secrets =
            new MemorySecrets(
                new Dictionary<string, string>
                {
                    [OsTicketConnections.ApiKey("lab").Key] = "EXAMPLE-KEY-8e1f"
                });

        var (service, _, _) = Create(configuration, secrets);

        var status = await service.GetAsync();

        var saved =
            Assert.Single(status.SavedConnections, connection => connection.Kind == "osTicket");

        Assert.Equal("lab", saved.Name);
        Assert.Contains(saved.Details, detail => detail.Label == "API key");
        Assert.DoesNotContain("EXAMPLE-KEY-8e1f", JsonSerializer.Serialize(status));
    }
}
