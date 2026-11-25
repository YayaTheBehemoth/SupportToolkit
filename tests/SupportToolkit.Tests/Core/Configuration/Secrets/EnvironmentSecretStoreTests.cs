using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;

namespace SupportToolkit.Tests.Core.Configuration.Secrets;

/// <summary>
/// On systems without a supported secret store, secrets are read from
/// environment variables and never stored. Nothing here reads the real
/// environment: the reader is replaced.
/// </summary>
public class EnvironmentSecretStoreTests
{
    private const string Value = "EXAMPLE-SECRET-VALUE-7d1e";

    private static EnvironmentSecretStore Store(
        Dictionary<string, string?> environment) =>
        new(name => environment.TryGetValue(name, out var value) ? value : null);

    [Fact]
    public void AKey_MapsToOneDocumentedVariable()
    {
        Assert.Equal(
            "SUPPORTTOOLKIT_SECRET__ZENDESK__TRIAL__CLIENT_SECRET",
            EnvironmentSecretStore.VariableName("zendesk:trial:client-secret"));

        // Every character other than A-Z and 0-9 is written as '_'.
        Assert.Equal(
            "SUPPORTTOOLKIT_SECRET__ACRONIS__PROD_EU_2__CLIENT_SECRET",
            EnvironmentSecretStore.VariableName(
                SupportToolkitSecretKeys.AcronisClientSecret("prod-eu.2")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("zendesk:trial")]
    [InlineData("zendesk::client-secret")]
    [InlineData("a:b:c:d")]
    public void AMalformedKey_IsRefused(string key)
    {
        Assert.Throws<ArgumentException>(
            () => EnvironmentSecretStore.VariableName(key));
    }

    [Fact]
    public async Task ASetVariable_IsRead_AndAMissingOrBlankOne_IsNull()
    {
        var key = SupportToolkitSecretKeys.ZendeskClientSecret("trial");

        var set =
            Store(new() { ["SUPPORTTOOLKIT_SECRET__ZENDESK__TRIAL__CLIENT_SECRET"] = $"  {Value} " });

        var blank =
            Store(new() { ["SUPPORTTOOLKIT_SECRET__ZENDESK__TRIAL__CLIENT_SECRET"] = "   " });

        Assert.Equal(Value, await set.GetSecretAsync(key));
        Assert.Null(await blank.GetSecretAsync(key));
        Assert.Null(await Store([]).GetSecretAsync(key));
    }

    [Fact]
    public async Task NothingIsEverStored_AndTheRefusalNamesTheVariable_NotTheValue()
    {
        var store = Store([]);
        var key = SupportToolkitSecretKeys.AcronisClientSecret("lab");

        Assert.False(((ISecretStore)store).CanStoreSecrets);

        var set =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SetSecretAsync(key, Value));

        var remove =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.RemoveSecretAsync(key));

        foreach (var message in new[] { set.Message, remove.Message })
        {
            Assert.Contains("SUPPORTTOOLKIT_SECRET__ACRONIS__LAB__CLIENT_SECRET", message);
            Assert.DoesNotContain(Value, message);
        }
    }

    [Fact]
    public void TheStore_IsChosenByOperatingSystem()
    {
        Assert.IsType<WindowsCredentialStore>(
            SecretStoreComposition.Create(isWindows: () => true));

        Assert.IsType<EnvironmentSecretStore>(
            SecretStoreComposition.Create(isWindows: () => false));
    }

    [Fact]
    public void AStoreThatKeepsSecrets_StatesNoOutsideLocation()
    {
        ISecretStore windows = new WindowsCredentialStore();

        Assert.True(windows.CanStoreSecrets);
        Assert.Null(windows.ExternalLocation("acronis:lab:client-secret"));
    }

    // ---------------------------------------------------------------
    // Clashes: two connections of one provider, one variable.
    // ---------------------------------------------------------------

    private static SupportToolkitConfiguration Clashing()
    {
        var configuration = FullConfiguration();

        configuration.Connections.Acronis["prod-eu"] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl = "https://example.invalid",
                ClientId = "example"
            };

        configuration.Connections.Acronis["prod_eu"] =
            new AcronisConnectionConfiguration
            {
                DatacenterUrl = "https://example.invalid",
                ClientId = "example"
            };

        configuration.Profiles["work"].AcronisConnection = "prod-eu";

        return configuration;
    }

    [Fact]
    public void AClash_IsFound_OnlyForAStoreThatReadsFromOutside()
    {
        var clash =
            Assert.Single(
                SecretLocationClashes.Find(Clashing(), Store([])));

        Assert.Equal("Acronis", clash.Provider);
        Assert.Equal(["prod-eu", "prod_eu"], clash.ConnectionNames);
        Assert.Equal("SUPPORTTOOLKIT_SECRET__ACRONIS__PROD_EU__CLIENT_SECRET", clash.Location);

        Assert.Empty(
            SecretLocationClashes.Find(Clashing(), new MemorySecrets([])));
    }

    [Fact]
    public async Task AClashingSecret_IsRefused_NeverSentToTheOtherConnection()
    {
        var secrets =
            Store(new() { ["SUPPORTTOOLKIT_SECRET__ACRONIS__PROD_EU__CLIENT_SECRET"] = Value });

        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(Clashing()),
                secrets,
                _ => null);

        var error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetAcronisConnectionAsync());

        Assert.Contains("'prod-eu' and 'prod_eu'", error.Message);
        Assert.DoesNotContain(Value, error.Message);
    }

    [Fact]
    public async Task WithoutAClash_TheConnectionReadsItsVariable()
    {
        var secrets =
            Store(new() { ["SUPPORTTOOLKIT_SECRET__ACRONIS__ACR__CLIENT_SECRET"] = Value });

        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(FullConfiguration()),
                secrets,
                _ => null);

        var connection = await resolver.GetAcronisConnectionAsync();

        Assert.Equal(Value, connection.ClientSecret);
    }

    // ---------------------------------------------------------------
    // A missing secret: the advice fits the store.
    // ---------------------------------------------------------------

    [Fact]
    public async Task AMissingSecret_NamesTheVariable_NotTheWizard()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(FullConfiguration()),
                Store([]),
                _ => null);

        var acronis =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetAcronisConnectionAsync());

        var zendesk =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetZendeskConnectionAsync());

        Assert.Contains(
            "Set SUPPORTTOOLKIT_SECRET__ACRONIS__ACR__CLIENT_SECRET or provide ACRONIS_CLIENT_SECRET.",
            acronis.Message);
        Assert.Contains(
            "Set SUPPORTTOOLKIT_SECRET__ZENDESK__ZD__CLIENT_SECRET or provide ZENDESK_CLIENT_SECRET.",
            zendesk.Message);
        Assert.DoesNotContain("configure profile", acronis.Message);
        Assert.DoesNotContain("configure profile", zendesk.Message);
    }

    [Fact]
    public async Task AMissingSecret_InAStoreThatKeepsSecrets_PointsToTheWizard()
    {
        var resolver =
            new SupportToolkitConfigurationResolver(
                new MemoryStore(FullConfiguration()),
                new MemorySecrets([]),
                _ => null);

        var acronis =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetAcronisConnectionAsync());

        Assert.Contains(
            "Run SupportToolkit configure profile work or provide ACRONIS_CLIENT_SECRET.",
            acronis.Message);
    }

    [Fact]
    public async Task Status_NamesTheStore_AndTheVariables_NeverAValue()
    {
        var store = new MemoryStore(Clashing());

        var secrets =
            Store(new() { ["SUPPORTTOOLKIT_SECRET__ZENDESK__ZD__CLIENT_SECRET"] = Value });

        var resolver =
            new SupportToolkitConfigurationResolver(store, secrets, _ => null);

        var status =
            await new ConfigurationStatusService(store, resolver, secrets, _ => null)
                .GetAsync();

        Assert.Contains("environment variables", status.SecretStore);
        Assert.Contains(status.SecretClashes!, clash => clash.Contains("'prod-eu' and 'prod_eu'"));

        var details =
            status.SavedConnections
                .SelectMany(connection => connection.Details)
                .Where(detail => detail.Label == "Client secret")
                .Select(detail => detail.Value)
                .ToArray();

        Assert.Contains("configured (SUPPORTTOOLKIT_SECRET__ZENDESK__ZD__CLIENT_SECRET)", details);
        Assert.Contains("not configured (SUPPORTTOOLKIT_SECRET__ACRONIS__ACR__CLIENT_SECRET)", details);
        Assert.DoesNotContain(details, detail => detail.Contains(Value));
    }
}
