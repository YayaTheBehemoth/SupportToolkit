using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class ZendeskDestinationConfirmationTests
{
    [Fact]
    public async Task Preflight_ReportsActualInstanceAndDestinationWithoutApiCall()
    {
        var workflow = CreateWorkflow();

        var destination = await workflow.GetTicketSubmissionTargetAsync();

        Assert.Equal("fixture-trial.zendesk.com", destination.InstanceHost);
        Assert.Equal("dev", destination.DestinationName);
        Assert.Equal(12345, destination.GroupId);
    }

    [Fact]
    public async Task ChangedDestination_RejectsBeforeAnyTicketWrite()
    {
        var workflow = CreateWorkflow();
        var report = new BackupAggregatorFixtureFactory().BuildReport();
        var staleDestination = new ZendeskTicketDestinationPreview(
            "different.zendesk.com", "dev", 12345);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.SubmitTicketToConfirmedDestinationAsync(
                report, staleDestination));

        Assert.Contains("changed since confirmation", failure.Message);
        Assert.Contains("No ticket was submitted", failure.Message);
    }

    [Fact]
    public void SameDestination_MatchesCaseInsensitivelyButNotDifferentGroups()
    {
        var first = new ZendeskTicketDestinationPreview(
            "fixture-trial.zendesk.com", "dev", 12345);

        Assert.True(first.IsSameAs(new ZendeskTicketDestinationPreview(
            "FIXTURE-TRIAL.ZENDESK.COM", "DEV", 12345)));

        Assert.False(first.IsSameAs(new ZendeskTicketDestinationPreview(
            "fixture-trial.zendesk.com", "dev", 54321)));

        Assert.False(first.IsSameAs(new ZendeskTicketDestinationPreview(
            "fixture-trial.zendesk.com", "operations", 12345)));
    }

    [Fact]
    public async Task MissingDestination_RejectsDuringPreflight()
    {
        var workflow = CreateWorkflow(withDestination: false);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => workflow.GetTicketSubmissionTargetAsync());

        Assert.Contains("destination", failure.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static BackupAggregatorWorkflow CreateWorkflow(bool withDestination = true)
    {
        var profile = new SupportToolkitProfile
        {
            ZendeskConnection = "trial",
            ZendeskDestination = withDestination ? "dev" : null
        };

        var config = new SupportToolkitConfiguration
        {
            ActiveProfile = "fixture-profile",
            Profiles = new Dictionary<string, SupportToolkitProfile>
            {
                ["fixture-profile"] = profile
            }
        };

        config.Connections.Zendesk["trial"] =
            new ZendeskConnectionConfiguration
            {
                Subdomain = "fixture-trial",
                ClientId = "fixture-client",
                SubmitterId = 900000000001,
                RequesterId = 900000000002,
                Destinations = new Dictionary<string, ZendeskDestinationConfiguration>
                {
                    ["dev"] = new ZendeskDestinationConfiguration
                    {
                        GroupId = 12345
                    }
                }
            };

        var resolver = new SupportToolkitConfigurationResolver(
            new TestConfigurationStore(config),
            new TestSecretStore(),
            _ => null);

        return new BackupAggregatorWorkflow(resolver);
    }

    private sealed class TestConfigurationStore : ISupportToolkitConfigurationStore
    {
        private readonly SupportToolkitConfiguration _configuration;

        public TestConfigurationStore(SupportToolkitConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string ConfigurationPath => "fixture-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_configuration);
        }

        public Task SaveAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestSecretStore : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(
                key == SupportToolkitSecretKeys.ZendeskClientSecret("trial")
                    ? "fixture-secret"
                    : null);
        }

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
