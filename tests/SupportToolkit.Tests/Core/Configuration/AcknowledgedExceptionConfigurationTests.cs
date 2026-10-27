using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests;

public class AcknowledgedExceptionConfigurationTests
{
    private const string Module = "backup-aggregator";

    [Fact]
    public async Task ValidEntry_IsApplied()
    {
        var settings =
            await ResolveAsync(
                Entry(
                    " aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ",
                    "tenantActiveSubscriptionInactive",
                    "Known data error.",
                    "2026-12-31",
                    "reviewer"));

        Assert.Empty(settings.Notices);

        var acknowledgement = Assert.Single(settings.Acknowledgements);

        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", acknowledgement.AcronisTenantId);
        Assert.Equal(DiscrepancyType.TenantActiveSubscriptionInactive, acknowledgement.Type);
        Assert.Equal(new DateOnly(2026, 12, 31), acknowledgement.ExpiresOn);
        Assert.Equal("reviewer", acknowledgement.AcknowledgedBy);
    }

    [Theory]
    [InlineData("", "TenantActiveSubscriptionInactive", "Reason", "2026-12-31", "has no tenant ID")]
    [InlineData("t1", "NoSuchType", "Reason", "2026-12-31", "unknown discrepancy type")]
    [InlineData("t1", "1", "Reason", "2026-12-31", "unknown discrepancy type")]
    [InlineData("t1", "TenantActiveSubscriptionInactive", " ", "2026-12-31", "has no reason")]
    [InlineData("t1", "TenantActiveSubscriptionInactive", "Reason", "31-12-2026", "no valid expiry date")]
    [InlineData("t1", "TenantActiveSubscriptionInactive", "Reason", "", "no valid expiry date")]
    public async Task InvalidEntry_IsNotAppliedAndIsReported(
        string tenantId,
        string type,
        string reason,
        string expiresOn,
        string expectedProblem)
    {
        var settings =
            await ResolveAsync(
                Entry(tenantId, type, reason, expiresOn));

        Assert.Empty(settings.Acknowledgements);

        var notice = Assert.Single(settings.Notices);

        Assert.Contains("Acknowledged exception 1", notice);
        Assert.Contains(expectedProblem, notice);
        Assert.Contains("not applied", notice);
    }

    [Fact]
    public async Task InvalidEntry_DoesNotAffectValidOnes()
    {
        var settings =
            await ResolveAsync(
                Entry("t1", "Bogus", "Reason", "2026-12-31"),
                Entry("t2", "MonitoredWithoutEntitlement", "Reason", "2026-12-31"));

        Assert.Single(settings.Acknowledgements);
        Assert.Contains("Acknowledged exception 1", Assert.Single(settings.Notices));
    }

    [Fact]
    public async Task NoEntries_IsEmpty()
    {
        var settings = await ResolveAsync();

        Assert.Empty(settings.Acknowledgements);
        Assert.Empty(settings.Notices);
    }

    private static AcknowledgedExceptionConfiguration Entry(
        string tenantId,
        string type,
        string reason,
        string expiresOn,
        string? acknowledgedBy = null)
    {
        return new AcknowledgedExceptionConfiguration
        {
            TenantId = tenantId,
            DiscrepancyType = type,
            Reason = reason,
            ExpiresOn = expiresOn,
            AcknowledgedBy = acknowledgedBy
        };
    }

    private static Task<AcknowledgementSettings> ResolveAsync(
        params AcknowledgedExceptionConfiguration[] entries)
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",

                Profiles =
                {
                    ["work"] =
                        new SupportToolkitProfile
                        {
                            MonitoringScope =
                            {
                                [Module] =
                                    new ModuleMonitoringScopeConfiguration
                                    {
                                        AcknowledgedExceptions = [.. entries]
                                    }
                            }
                        }
                }
            };

        return new SupportToolkitConfigurationResolver(
                new Store(configuration),
                new NoSecrets(),
                _ => null)
            .GetAcknowledgementsAsync(Module);
    }

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(configuration);

        public Task SaveAsync(
            SupportToolkitConfiguration value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoSecrets : ISecretStore
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
