using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupAggregatorWorkflowTests
{
    [Fact]
    public async Task ReviewAllTenantsAsync_InFixtureMode_ReturnsCompleteFixtureEstate()
    {
        var workflow =
            CreateFixtureWorkflow();

        var report =
            await workflow
                .ReviewAllTenantsAsync();

        Assert.Equal(
            5,
            report.TenantCount
        );

        Assert.Equal(
            15,
            report.RowsChecked
        );
    }

    [Fact]
    public async Task ReviewTenantAsync_InFixtureMode_ReturnsRequestedTenant()
    {
        var workflow =
            CreateFixtureWorkflow();

        var report =
            await workflow
                .ReviewTenantAsync(
                    "contoso workshop"
                );

        var tenant =
            Assert.Single(
                report.Tenants
            );

        Assert.Equal(
            "Contoso Workshop",
            tenant.TenantName
        );
    }

    [Fact]
    public async Task ReviewTenantAsync_WhenFixtureTenantDoesNotExist_Throws()
    {
        var workflow =
            CreateFixtureWorkflow();

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    workflow
                        .ReviewTenantAsync(
                            "Definitely Not A Tenant"
                        )
            );

        Assert.Contains(
            "Definitely Not A Tenant",
            exception.Message
        );
    }

    private static BackupAggregatorWorkflow
        CreateFixtureWorkflow()
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile =
                    "local-dev",

                Profiles =
                    new Dictionary<
                        string,
                        SupportToolkitProfile>
                    {
                        ["local-dev"] =
                            new SupportToolkitProfile
                            {
                                Modules =
                                    new Dictionary<
                                        string,
                                        SupportToolkitMode>
                                    {
                                        ["backup-aggregator"] =
                                            SupportToolkitMode.Fixture
                                    }
                            }
                    }
            };

        var resolver =
            new SupportToolkitConfigurationResolver(
                new TestConfigurationStore(
                    configuration
                ),
                new EmptySecretStore(),
                _ => null
            );

        return new BackupAggregatorWorkflow(
            resolver
        );
    }

    private sealed class TestConfigurationStore
        : ISupportToolkitConfigurationStore
    {
        private SupportToolkitConfiguration
            _configuration;

        public string ConfigurationPath =>
            "test.config.json";

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

    private sealed class EmptySecretStore
        : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<string?>(
                null
            );
        }

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }
}