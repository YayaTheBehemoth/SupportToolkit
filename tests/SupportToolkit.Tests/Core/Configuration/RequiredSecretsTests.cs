using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Tests.Ui.Web;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// After an import, the secrets the connections need are asked for. Which
/// ones are decided here: only for connections a profile uses, and only for
/// the kinds of connection that have a client secret.
/// </summary>
public class RequiredSecretsTests
{
    private static SupportToolkitConfiguration Configuration()
    {
        var configuration = new SupportToolkitConfiguration { ActiveProfile = "work" };

        configuration.Connections.Acronis["acr"] =
            new AcronisConnectionConfiguration { ClientId = "example" };

        configuration.Connections.Acronis["unused-acr"] =
            new AcronisConnectionConfiguration { ClientId = "example" };

        configuration.Connections.Zendesk["zd"] =
            new ZendeskConnectionConfiguration { Subdomain = "example", ClientId = "example" };

        configuration.Connections.ZendeskDeletion["del"] =
            new ZendeskDeletionConnectionConfiguration { Subdomain = "example", ClientId = "example" };

        configuration.Connections.BusinessCentral["bc"] =
            new BusinessCentralConnectionConfiguration { TenantId = "t", ClientId = "c" };

        configuration.Profiles["work"] =
            new SupportToolkitProfile
            {
                AcronisConnection = "acr",
                ZendeskConnection = "zd",
                ZendeskDeletionConnection = "del",
                BusinessCentralConnection = "bc"
            };

        return configuration;
    }

    [Fact]
    public async Task ABlankMachine_NeedsTheAcronisAndZendeskSecrets_Only()
    {
        var missing =
            await RequiredSecrets.FindMissingAsync(
                Configuration(),
                new StatusTestSupport.MemorySecrets([]));

        Assert.Equal(
            ["Acronis:acr", "Zendesk (ticketing):zd"],
            missing.Select(item => $"{item.Provider}:{item.ConnectionName}"));
    }

    [Fact]
    public async Task ASecretAlreadyStored_IsNotAskedFor()
    {
        var secrets =
            new StatusTestSupport.MemorySecrets(
                new Dictionary<string, string>
                {
                    [SupportToolkitSecretKeys.AcronisClientSecret("acr")] = "stored"
                });

        var missing =
            await RequiredSecrets.FindMissingAsync(Configuration(), secrets);

        var only = Assert.Single(missing);

        Assert.Equal("zd", only.ConnectionName);
        Assert.Equal(SupportToolkitSecretKeys.ZendeskClientSecret("zd"), only.Key);
    }

    [Fact]
    public async Task EverySecretStored_NeedsNothing()
    {
        var secrets =
            new StatusTestSupport.MemorySecrets(
                new Dictionary<string, string>
                {
                    [SupportToolkitSecretKeys.AcronisClientSecret("acr")] = "a",
                    [SupportToolkitSecretKeys.ZendeskClientSecret("zd")] = "b"
                });

        Assert.Empty(
            await RequiredSecrets.FindMissingAsync(Configuration(), secrets));
    }

    [Fact]
    public async Task ASavedConnectionNoProfileUses_IsNotAskedFor()
    {
        var missing =
            await RequiredSecrets.FindMissingAsync(
                Configuration(),
                new StatusTestSupport.MemorySecrets([]));

        Assert.DoesNotContain(missing, item => item.ConnectionName == "unused-acr");
    }

    [Fact]
    public async Task ABlankStoredValue_CountsAsMissing()
    {
        var secrets =
            new StatusTestSupport.MemorySecrets(
                new Dictionary<string, string>
                {
                    [SupportToolkitSecretKeys.AcronisClientSecret("acr")] = "   "
                });

        var missing =
            await RequiredSecrets.FindMissingAsync(Configuration(), secrets);

        Assert.Contains(missing, item => item.ConnectionName == "acr");
    }

    [Fact]
    public async Task AProfileNamingAConnectionThatDoesNotExist_AsksForNothingForIt()
    {
        var configuration = Configuration();

        configuration.Profiles["broken"] =
            new SupportToolkitProfile { AcronisConnection = "not-saved" };

        var missing =
            await RequiredSecrets.FindMissingAsync(
                configuration,
                new StatusTestSupport.MemorySecrets([]));

        Assert.DoesNotContain(missing, item => item.ConnectionName == "not-saved");
    }

    [Fact]
    public async Task TheResult_NeverContainsASecretValue()
    {
        var secrets =
            new StatusTestSupport.MemorySecrets(
                new Dictionary<string, string>
                {
                    [SupportToolkitSecretKeys.AcronisClientSecret("acr")] = "SECRET-VALUE-XYZ"
                });

        var missing =
            await RequiredSecrets.FindMissingAsync(Configuration(), secrets);

        Assert.DoesNotContain("SECRET-VALUE-XYZ", string.Concat(missing));
    }
}
