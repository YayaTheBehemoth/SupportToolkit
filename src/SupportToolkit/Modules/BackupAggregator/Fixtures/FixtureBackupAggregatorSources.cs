using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Fixture mode for the all-tenant review: the synthetic report. No
/// external request is ever made.
/// </summary>
public sealed class FixtureBackupAggregatorSources : IBackupAggregatorSources
{
    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Fixture);

    public Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new BackupAggregatorFixtureFactory()
                .BuildReport());

    public Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken) =>
        Task.FromResult(
            ReviewFixtureTenant(
                tenantName));

    private static AggregatedBackupReport ReviewFixtureTenant(
        string tenantName)
    {
        var completeReport =
            new BackupAggregatorFixtureFactory()
                .BuildReport();

        var matches =
            completeReport.Tenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.TenantName,
                            tenantName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Fixture tenant '{tenantName}' was not found."
            );
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"More than one fixture tenant named " +
                $"'{tenantName}' was found."
            );
        }

        return new AggregatedBackupReport
        {
            Tenants =
            [
                matches[0]
            ]
        };
    }
}

/// <summary>
/// Fixture mode for the monitored review: synthetic subscriptions,
/// tenants and inventory, with confirmed mappings for the synthetic
/// identities. No external request is ever made.
/// </summary>
public sealed class FixtureScopedReviewSources : IScopedReviewSources
{
    public Task<ScopedReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new ScopedReviewAvailability(SupportToolkitMode.Fixture, true, null));

    public async Task<ScopedReviewInputs> GetInputsAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var inventory =
            new HealthyFixtureInventoryProvider(asOfDate);

        var fixtureMappings =
            await CreateFixtureMappingRepositoryAsync(
                cancellationToken);

        return new ScopedReviewInputs(
            [SmartSubscriptionFixtureFactory
                .FixtureMonitoringItemNumber],
            new ConfirmedMappingIdentitySource(fixtureMappings),
            _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                new FixtureSmartSubscriptionEvidenceSource()),
            null,
            _ => Task.FromResult(
                ScopedReviewFixtureInventory.CreateRuntime(
                    inventory)),
            MappingRepository: fixtureMappings);
    }

    /// <summary>
    /// Synthetic Confirmed mappings matching the fixture identities.
    /// </summary>
    private static async Task<ICustomerMappingRepository>
        CreateFixtureMappingRepositoryAsync(
            CancellationToken cancellationToken)
    {
        var repository =
            new InMemoryCustomerMappingRepository();

        foreach (var identity in await new FixtureMonitoringIdentitySource()
            .GetIdentitiesAsync(cancellationToken))
        {
            await repository.AddAsync(
                new CustomerMappingRecord(
                    Guid.NewGuid(),
                    identity.TenantId,
                    identity.BusinessCentralCustomerNumber,
                    CustomerMappingState.Confirmed,
                    CustomerMatchMethod.Manual),
                cancellationToken);
        }

        return repository;
    }
}
