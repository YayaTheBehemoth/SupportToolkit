using System.Collections.Concurrent;
using SupportToolkit.Core.MonitoringScope;
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
/// Synthetic Acronis customer tenants for fixture-mode scoped review.
/// </summary>
public sealed class FixtureCustomerTenantProvider
    : IAcronisTenantProvider
{
    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            MonitoringTenantSelectionFixtureFactory
                .Create()
                .Tenants);
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
