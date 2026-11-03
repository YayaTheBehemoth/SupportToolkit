using System.Collections.Concurrent;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Production;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Synthetic subscription evidence for fixture-mode scoped review.
/// No external request is ever made.
/// </summary>
public sealed class FixtureSmartSubscriptionEvidenceSource
    : ISmartSubscriptionEvidenceSource
{
    public Task<SmartSubscriptionEvidence> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        var subscriptions =
            SmartSubscriptionFixtureFactory.Create();

        return Task.FromResult(
            new SmartSubscriptionEvidence(
                subscriptions.Headers,
                subscriptions.Lines));
    }
}

/// <summary>
/// The fixture-mode scoped review's inventory: synthetic tenants and
/// inventory read through the real Acronis adapter, so fixture mode
/// exercises the same tenant resolution and normalization as production.
/// </summary>
public static class ScopedReviewFixtureInventory
{
    public static ScopedInventoryRuntime CreateRuntime(
        HealthyFixtureInventoryProvider inventory) =>
        new(
            AcronisBackupInventory.CreateReviewService(
                new AcronisTenantResolver(
                    new FixtureCustomerTenantProvider()),
                inventory,
                inventory,
                new BackupInventoryNormalizer()),
            null);
}

/// <summary>
/// Synthetic Acronis customer tenants for fixture-mode scoped review.
/// </summary>
public sealed class FixtureCustomerTenantProvider
    : IAcronisTenantProvider
{
    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        // This fakes Acronis, so it hands out Acronis tenants.
        return Task.FromResult<IReadOnlyList<TenantDto>>(
            MonitoringTenantSelectionFixtureFactory
                .Create()
                .Tenants
                .Select(
                    tenant => new TenantDto
                    {
                        Id = tenant.Id,
                        Name = tenant.Name,
                        Kind = tenant.Kind,
                        DeletedAt = tenant.DeletedAt,
                        Enabled = tenant.Enabled
                    })
                .ToArray());
    }
}

/// <summary>
/// Synthetic inventory that records which tenants were requested, so
/// scoped execution can be checked against its selection.
/// </summary>
public sealed class HealthyFixtureInventoryProvider
    : IAcronisMicrosoft365InventoryProvider,
      IAcronisDeviceInventoryProvider
{
    private readonly DateTimeOffset _backupTime;

    public ConcurrentBag<string> Microsoft365Requests { get; } =
        new();

    public ConcurrentBag<string> DeviceRequests { get; } =
        new();

    public HealthyFixtureInventoryProvider(
        DateOnly asOfDate)
    {
        _backupTime =
            new DateTimeOffset(
                asOfDate.ToDateTime(
                    new TimeOnly(12, 0)
                ),
                TimeSpan.Zero
            );
    }

    public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
        GetResourcesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        Microsoft365Requests.Add(
            tenantId
        );

        return Task.FromResult<
            IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
            []
        );
    }

    Task<IReadOnlyList<AcronisDeviceResourceDto>>
        IAcronisDeviceInventoryProvider.GetResourcesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken)
    {
        DeviceRequests.Add(
            tenantId
        );

        IReadOnlyList<AcronisDeviceResourceDto> resources =
        [
            new AcronisDeviceResourceDto
            {
                Id = "fixture-device",
                Name = "Fixture Backup Device",

                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "ok",
                        LastBackup = _backupTime,
                        LastSuccessBackup = _backupTime,
                        AppliedPolicyNames = "Fixture Backup"
                    }
            }
        ];

        return Task.FromResult(resources);
    }
}
