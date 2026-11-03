using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

[Collection("ConsoleOutput")]
public class BackupAggregatorScopedWorkflowTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    public async Task FixtureMode_RunsSyntheticScopedReview_WithoutAnyConnection()
    {
        // No profile, no Business Central connection: fixture default.
        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(new SupportToolkitConfiguration()));

        var result = await workflow.ReviewAsync(Today);

        Assert.Equal(3, result.Selection.InventoryCandidates.Count);
        Assert.True(result.IsInventoryComplete);

        // The suspended fixture subscription is decided as not
        // entitled, so the synthetic scope is complete.
        Assert.True(result.IsScopeComplete);
        Assert.True(result.IsOverallComplete);

        var report = Assert.IsType<MonitoringScopeReport>(result.ScopeReport);

        Assert.True(report.ReconciliationRun);
        Assert.Equal(3, report.InScope.Count);
        Assert.Equal(2, report.Excluded.Count);
        Assert.Equal(5, report.AcronisCustomerTenants);
    }

    [Fact]
    public async Task ProductionMode_UsesConfiguredItemNumber_AndDisposesRuntime()
    {
        var lifetime = new TrackingLifetime();
        var inventory = new HealthyFixtureInventoryProvider(Today);

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(ProductionConfiguration()),
                new FixtureMonitoringIdentitySource(),
                _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                    new FixtureSmartSubscriptionEvidenceSource()),
                _ => Task.FromResult(
                    new ScopedInventoryRuntime(
                        new BackupInventoryReviewService(
                            new AcronisTenantResolver(
                                new FixtureCustomerTenantProvider()),
                            inventory,
                            inventory,
                            new BackupInventoryNormalizer()),
                        lifetime)));

        var result = await workflow.ReviewAsync(Today);

        Assert.Equal(3, result.Selection.InventoryCandidates.Count);
        Assert.True(lifetime.Disposed);
    }

    [Fact]
    public async Task ProductionMode_DifferentItemNumber_SelectsNoTenants_AndIsIncomplete()
    {
        var configuration = ProductionConfiguration("OTHER-ITEM");
        var inventory = new HealthyFixtureInventoryProvider(Today);

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(configuration),
                new FixtureMonitoringIdentitySource(),
                _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                    new FixtureSmartSubscriptionEvidenceSource()),
                _ => Task.FromResult(
                    new ScopedInventoryRuntime(
                        new BackupInventoryReviewService(
                            new AcronisTenantResolver(
                                new FixtureCustomerTenantProvider()),
                            inventory,
                            inventory,
                            new BackupInventoryNormalizer()),
                        null)));

        var result = await workflow.ReviewAsync(Today);

        // Nobody is evidenced as entitled: never reported as complete.
        Assert.Empty(result.Selection.InventoryCandidates);
        Assert.False(result.IsScopeComplete);
        Assert.False(result.IsOverallComplete);
        Assert.Empty(inventory.DeviceRequests);
    }

    [Fact]
    public async Task ProductionMode_MissingItemNumber_FailsBeforeIdentityOrBc()
    {
        var configuration = ProductionConfiguration();
        configuration.Profiles["work"].MonitoringScope.Clear();

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(configuration),
                new FixtureMonitoringIdentitySource());

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => workflow.ReviewAsync(Today));

        Assert.Contains("Monitoring item number", exception.Message);
    }

    [Theory]
    [InlineData("bc-work", true)]
    [InlineData(null, false)]
    public async Task IsEnabled_ReflectsProfileConnection(
        string? connection,
        bool expected)
    {
        var configuration = ProductionConfiguration();
        configuration.Profiles["work"].BusinessCentralConnection =
            connection;

        var workflow =
            new BackupAggregatorScopedWorkflow(Resolver(configuration));

        Assert.Equal(expected, await workflow.IsEnabledAsync());
    }

    [Fact]
    public async Task IsEnabled_IsFalseWithoutAnyProfile()
    {
        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(new SupportToolkitConfiguration()));

        Assert.False(await workflow.IsEnabledAsync());
    }

    [Fact]
    public async Task Module_ScopedReviewCommand_RunsInFixtureMode_AndLeavesDefaultsAlone()
    {
        var resolver = Resolver(new SupportToolkitConfiguration());

        var module =
            new BackupAggregatorModule(
                new BackupAggregatorWorkflow(resolver),
                new BackupAggregatorScopedWorkflow(resolver));

        var original = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var exit = await module.RunAsync(["scoped-review"]);
            var scoped = writer.ToString();

            // Not-entitled customers are decided, so the synthetic
            // scope is complete: exit code 0.
            Assert.Equal(0, exit);
            Assert.Contains("MONITORING SCOPE REPORT", scoped);
            Assert.Contains("IN SCOPE THIS RUN (3)", scoped);
            Assert.Contains("Monitoring scope complete:   YES", scoped);

            // The default view stays quiet: details need debug mode.
            Assert.DoesNotContain("EXCLUDED (", scoped);
            Assert.DoesNotContain("EXCEPTIONS", scoped);
            Assert.Contains("SUPPORTTOOLKIT_DEBUG=true", scoped);

            // The default fixture shortcut is unchanged by the feature.
            writer.GetStringBuilder().Clear();

            var defaultExit = await module.RunAsync([]);

            Assert.DoesNotContain("MONITORING SCOPE", writer.ToString());
            Assert.True(defaultExit is 0 or 2);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Module_ScopedTicketPreview_UsesTheCompleteScopedReview()
    {
        var resolver = Resolver(new SupportToolkitConfiguration());

        var module =
            new BackupAggregatorModule(
                new BackupAggregatorWorkflow(resolver),
                new BackupAggregatorScopedWorkflow(resolver));

        var original = Console.Out;

        try
        {
            using var writer = new StringWriter();
            Console.SetOut(writer);

            var exit =
                await module.RunAsync(["ticket", "preview", "--scoped"]);

            var text = writer.ToString();

            Assert.Equal(0, exit);
            Assert.Contains("SUPPORTTOOLKIT // BACKUP REVIEW", text);
            Assert.Contains("SCOPE", text);
            Assert.Contains("Monitored tenants only:", text);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Module_WithoutScopedWorkflow_RejectsScopedTicketCommands()
    {
        var resolver = Resolver(new SupportToolkitConfiguration());

        var module =
            new BackupAggregatorModule(
                new BackupAggregatorWorkflow(resolver));

        var originalError = Console.Error;

        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            Assert.Equal(
                1,
                await module.RunAsync(["ticket", "preview", "--scoped"]));

            Assert.Equal(
                1,
                await module.RunAsync(["ticket", "submit", "--scoped"]));

            Assert.Contains("not available", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Module_WithoutScopedWorkflow_RejectsTheCommand()
    {
        var resolver = Resolver(new SupportToolkitConfiguration());

        var module =
            new BackupAggregatorModule(
                new BackupAggregatorWorkflow(resolver));

        var originalError = Console.Error;

        try
        {
            using var error = new StringWriter();
            Console.SetError(error);

            Assert.Equal(1, await module.RunAsync(["scoped-review"]));
            Assert.Contains("not available", error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Reporter_SeparatesScopeFromInventoryCompleteness()
    {
        var tenants =
            MonitoringTenantSelectionFixtureFactory.Create();

        var selection =
            new MonitoringTenantSelector().Select(
                tenants.Tenants,
                [],
                tenants.Identities);

        var result =
            new ScopedBackupInventoryReviewResult(
                selection,
                new SupportToolkit.Modules.BackupAggregator.Models
                    .AggregatedBackupReport { Tenants = [] });

        using var writer = new StringWriter();

        new ConsoleScopedReviewReporter().Write(writer, result);

        var text = writer.ToString();

        Assert.Contains("Monitoring scope complete:   NO", text);
        Assert.Contains(
            "a ticket cannot be created until the reasons above are " +
            "resolved",
            text);
        Assert.Contains("UNRESOLVED SCOPE", text);
    }

    private static SupportToolkitConfiguration ProductionConfiguration(
        string itemNumber = SmartSubscriptionFixtureFactory
            .FixtureMonitoringItemNumber)
    {
        return new SupportToolkitConfiguration
        {
            ActiveProfile = "work",

            Profiles =
            {
                ["work"] =
                    new SupportToolkitProfile
                    {
                        BusinessCentralConnection = "bc-work",

                        Modules =
                        {
                            ["backup-aggregator"] =
                                SupportToolkitMode.Production
                        },

                        MonitoringScope =
                        {
                            ["backup-aggregator"] =
                                new ModuleMonitoringScopeConfiguration
                                {
                                    MonitoringItemNumber = itemNumber
                                }
                        }
                    }
            }
        };
    }

    private static SupportToolkitConfigurationResolver Resolver(
        SupportToolkitConfiguration configuration)
    {
        return new SupportToolkitConfigurationResolver(
            new Store(configuration),
            new NoSecrets(),
            _ => null);
    }

    private sealed class TrackingLifetime : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
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

    private sealed class NoSecrets : ISecretStore
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
            throw new InvalidOperationException("unexpected");
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("unexpected");
        }
    }
}
