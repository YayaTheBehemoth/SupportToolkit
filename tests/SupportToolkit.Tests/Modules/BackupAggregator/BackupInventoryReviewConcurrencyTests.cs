using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupInventoryReviewConcurrencyTests
{
    [Fact]
    public async Task ReviewAllTenantsAsync_BoundsConcurrencyAtThree()
    {
        var tenants =
            Enumerable.Range(
                    1,
                    9
                )
                .Select(
                    index =>
                        new TenantDto
                        {
                            Id =
                                $"00000000-0000-0000-0000-{index:000000000000}",

                            Name =
                                $"Customer {index:00}",

                            Kind =
                                "customer"
                        }
                )
                .ToList();

        var microsoft365Provider =
            new ConcurrencyTrackingMicrosoft365Provider();

        var service =
            new BackupInventoryReviewService(
                new AcronisTenantResolver(
                    new StubTenantProvider(
                        tenants
                    )
                ),
                microsoft365Provider,
                new EmptyDeviceProvider(),
                new BackupInventoryNormalizer()
            );

        var report =
            await service.ReviewAllTenantsAsync();

        Assert.Equal(
            3,
            microsoft365Provider.MaxObservedConcurrency
        );

        Assert.Equal(
            tenants.Count,
            report.TenantCount
        );

        Assert.Equal(
            tenants.Select(
                tenant =>
                    tenant.Name
            ),
            report.Tenants.Select(
                tenant =>
                    tenant.TenantName
            )
        );
    }

    private sealed class StubTenantProvider
        : IAcronisTenantProvider
    {
        private readonly IReadOnlyList<TenantDto>
            _tenants;

        public StubTenantProvider(
            IReadOnlyList<TenantDto> tenants)
        {
            _tenants = tenants;
        }

        public Task<IReadOnlyList<TenantDto>>
            GetTenantsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _tenants
            );
        }
    }

    private sealed class ConcurrencyTrackingMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        private readonly object _sync =
            new();

        private int _active;

        public int MaxObservedConcurrency { get; private set; }

        public async Task<
            IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _active++;

                MaxObservedConcurrency =
                    Math.Max(
                        MaxObservedConcurrency,
                        _active
                    );
            }

            try
            {
                /*
                 * Earlier tenant IDs deliberately wait longer so completion
                 * order differs from input order. This verifies that bounded
                 * concurrency does not make report ordering nondeterministic.
                 */
                var finalDigit =
                    int.Parse(
                        tenantId[^1].ToString()
                    );

                await Task.Delay(
                    TimeSpan.FromMilliseconds(
                        (10 - finalDigit) * 20
                    ),
                    cancellationToken
                );

                return [];
            }
            finally
            {
                lock (_sync)
                {
                    _active--;
                }
            }
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
                IReadOnlyList<AcronisDeviceResourceDto>>(
                []
            );
        }
    }
}