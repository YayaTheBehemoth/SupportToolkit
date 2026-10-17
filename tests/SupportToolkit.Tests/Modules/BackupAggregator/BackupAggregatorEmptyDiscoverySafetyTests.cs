
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupAggregatorEmptyDiscoverySafetyTests
{
    [Fact]
    public async Task ReviewAllTenantsAsync_ZeroTenants_Throws()
    {
        var service =
            CreateService([]);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ReviewAllTenantsAsync());

        Assert.Contains(
            "zero customer tenants",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewAllTenantsAsync_NoCustomerKinds_Throws()
    {
        var service =
            CreateService(
            [
                new TenantDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Synthetic Partner",
                    Kind = "partner"
                }
            ]);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ReviewAllTenantsAsync());

        Assert.Contains(
            "zero customer tenants",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReviewAllTenantsAsync_OneCustomerWithoutResources_IsValid()
    {
        var service =
            CreateService(
            [
                new TenantDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Synthetic Customer",
                    Kind = "customer"
                }
            ]);

        var report =
            await service.ReviewAllTenantsAsync();

        Assert.Equal(1, report.TenantsAttempted);
        Assert.Equal(1, report.TenantCount);
        Assert.Equal(0, report.RowsChecked);
        Assert.Equal(0, report.FailedTenantCount);
        Assert.True(report.IsTenantReviewComplete);
        Assert.True(report.IsFullyAccountedFor);
    }

    [Fact]
    public void CreateTicket_ZeroTenantReport_Throws()
    {
        var report =
            new AggregatedBackupReport
            {
                Tenants = []
            };

        var factory =
            new BackupReviewTicketDraftFactory();

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => factory.Create(report));

        Assert.Contains(
            "no customer tenants",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateTicket_ReviewedTenantWithoutResources_RemainsValid()
    {
        var report =
            new AggregatedBackupReport
            {
                Tenants =
                [
                    new TenantBackupReport
                    {
                        TenantName = "Synthetic Customer",
                        Entries = []
                    }
                ]
            };

        var factory =
            new BackupReviewTicketDraftFactory();

        var draft =
            factory.Create(report);

        Assert.Equal(
            "Backup review complete - no attention required",
            draft.Subject);

        Assert.Contains(
            "Tenants reviewed: 1/1",
            draft.Body);
    }

    private static BackupInventoryReviewService CreateService(
        IReadOnlyList<TenantDto> tenants)
    {
        return new BackupInventoryReviewService(
            new AcronisTenantResolver(
                new StubTenantProvider(tenants)),
            new EmptyMicrosoft365Provider(),
            new EmptyDeviceProvider(),
            new BackupInventoryNormalizer());
    }

    private sealed class StubTenantProvider : IAcronisTenantProvider
    {
        private readonly IReadOnlyList<TenantDto> _tenants;

        public StubTenantProvider(
            IReadOnlyList<TenantDto> tenants)
        {
            _tenants = tenants;
        }

        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_tenants);
        }
    }

    private sealed class EmptyMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<AcronisMicrosoft365ResourceDto>>([]);
        }
    }

    private sealed class EmptyDeviceProvider
        : IAcronisDeviceInventoryProvider
    {
        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<
                IReadOnlyList<AcronisDeviceResourceDto>>([]);
        }
    }
}
