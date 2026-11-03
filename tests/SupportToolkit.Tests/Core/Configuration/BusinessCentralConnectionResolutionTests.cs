using System.Text.Json;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Core.Configuration;

public class BusinessCentralConnectionResolutionTests
{
    [Fact]
    public async Task ResolvesActiveProfileConnection()
    {
        var resolver = CreateResolver(CreateConfiguration());

        var connection =
            await resolver.GetBusinessCentralConnectionAsync();

        Assert.Equal("Sandbox", connection.Environment);
        Assert.Equal("example", connection.ApiPublisher);
        Assert.Equal("Synthetic Company", connection.CompanyName);
    }

    [Fact]
    public async Task ConnectionNameLookup_IsCaseInsensitive()
    {
        var configuration = CreateConfiguration();
        configuration.Profiles["work"].BusinessCentralConnection =
            "  BC-Work ";

        var resolver = CreateResolver(configuration);

        await resolver.GetBusinessCentralConnectionAsync();
    }

    [Fact]
    public async Task BlankCompanyName_ResolvesToNull()
    {
        var configuration = CreateConfiguration();
        configuration.Connections.BusinessCentral["bc-work"].CompanyName =
            "  ";

        var resolver = CreateResolver(configuration);

        var connection =
            await resolver.GetBusinessCentralConnectionAsync();

        Assert.Null(connection.CompanyName);
    }

    [Fact]
    public async Task ProfileWithoutConnection_ExplainsHowToConfigure()
    {
        var configuration = CreateConfiguration();
        configuration.Profiles["work"].BusinessCentralConnection = null;

        var resolver = CreateResolver(configuration);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetBusinessCentralConnectionAsync());

        Assert.Contains("Business Central connection", exception.Message);
        Assert.Contains("configure profile work", exception.Message);
    }

    [Fact]
    public async Task MissingConnection_IsRejected()
    {
        var configuration = CreateConfiguration();
        configuration.Connections.BusinessCentral.Clear();

        var resolver = CreateResolver(configuration);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetBusinessCentralConnectionAsync());

        Assert.Contains("does not exist", exception.Message);
    }

    [Theory]
    [InlineData("TenantId")]
    [InlineData("ClientId")]
    [InlineData("Environment")]
    [InlineData("CompanyId")]
    [InlineData("ApiPublisher")]
    [InlineData("ApiGroup")]
    [InlineData("ApiVersion")]
    public async Task MissingRequiredValue_IsRejected(
        string field)
    {
        var configuration = CreateConfiguration();
        var connection =
            configuration.Connections.BusinessCentral["bc-work"];

        typeof(BusinessCentralConnectionConfiguration)
            .GetProperty(field)!
            .SetValue(connection, "  ");

        var resolver = CreateResolver(configuration);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => resolver.GetBusinessCentralConnectionAsync());

        Assert.Contains("is not configured", exception.Message);
    }

    [Fact]
    public async Task EnvironmentVariables_CannotOverrideTheConnection()
    {
        var configuration = CreateConfiguration();

        var resolver =
            CreateResolver(
                configuration,
                variable => "https://attacker.invalid");

        var connection =
            await resolver.GetBusinessCentralConnectionAsync();

        Assert.Equal("Sandbox", connection.Environment);
        Assert.DoesNotContain("attacker", connection.TenantId);
    }

    [Fact]
    public void Serialization_RoundTripsWithoutAnySecretField()
    {
        var json =
            JsonSerializer.Serialize(
                CreateConfiguration().Connections.BusinessCentral["bc-work"]);

        var names =
            JsonDocument.Parse(json)
                .RootElement
                .EnumerateObject()
                .Select(p => p.Name.ToLowerInvariant());

        Assert.DoesNotContain(
            names,
            name => name.Contains("secret")
                || name.Contains("token")
                || name.Contains("password"));
    }

    [Fact]
    public void ConfigurationWithoutBusinessCentralSection_StillLoadsEmpty()
    {
        // Configurations written before this section existed.
        var configuration =
            JsonSerializer.Deserialize<SupportToolkitConfiguration>(
                "{\"activeProfile\":\"work\",\"connections\":{}}",
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                })!;

        Assert.Empty(configuration.Connections.BusinessCentral);
    }

    private static SupportToolkitConfiguration CreateConfiguration()
    {
        return new SupportToolkitConfiguration
        {
            ActiveProfile = "work",

            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        BusinessCentralConnection = "bc-work"
                    }
            },

            Connections =
            {
                BusinessCentral =
                {
                    ["bc-work"] =
                        new BusinessCentralConnectionConfiguration
                        {
                            TenantId =
                                "11111111-1111-1111-1111-111111111111",
                            ClientId =
                                "22222222-2222-2222-2222-222222222222",
                            Environment = "Sandbox",
                            CompanyId =
                                "33333333-3333-3333-3333-333333333333",
                            CompanyName = "Synthetic Company",
                            ApiPublisher = "example",
                            ApiGroup = "monitoring",
                            ApiVersion = "v1.0"
                        }
                }
            }
        };
    }

    private static SupportToolkitConfigurationResolver CreateResolver(
        SupportToolkitConfiguration configuration,
        Func<string, string?>? environment = null)
    {
        return new SupportToolkitConfigurationResolver(
            new Store(configuration),
            new EmptySecrets(),
            environment ?? (_ => null));
    }

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(configuration);
        }

        public Task SaveAsync(
            SupportToolkitConfiguration value,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class EmptySecrets : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Business Central stores no secrets.");
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Business Central stores no secrets.");
        }
    }
}
