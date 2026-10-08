using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupInventoryReviewServiceTests
{
    [Fact]
    public async Task ReviewTenantAsync_CombinesBothInventoryDomains()
    {
        var microsoft365Resources =
            Enumerable.Range(
                    1,
                    41
                )
                .Select(
                    index =>
                        new AcronisMicrosoft365ResourceDto
                        {
                            Id = $"m365-{index}",
                            Name = $"M365 {index}",
                            HasProtections = true,
                            LastTaskStatus = "ok",
                            LastTaskState = "idle",
                            LastSuccessTime =
                                DateTimeOffset.Parse(
                                    "2026-10-08T12:00:00Z"
                                )
                        }
                )
                .ToList();

        var deviceResources =
            new List<AcronisDeviceResourceDto>();

        for (var index = 1; index <= 3; index++)
        {
            deviceResources.Add(
                Device(
                    $"device-{index}",
                    $"SERVER-{index}",
                    "idle"
                )
            );
        }

        deviceResources.Add(
            Device(
                "device-4",
                "SQL-SRV",
                "notProtected"
            )
        );

        var service =
            new BackupInventoryReviewService(
                new AcronisTenantResolver(
                    new StubTenantProvider()
                ),
                new StubMicrosoft365Provider(
                    microsoft365Resources
                ),
                new StubDeviceProvider(
                    deviceResources
                ),
                new BackupInventoryNormalizer()
            );

        var report =
            await service.ReviewTenantAsync(
                "Customer Alpha"
            );

        Assert.Equal(
            45,
            report.RowsChecked
        );
        Assert.Equal(
            44,
            report.HealthyRowsSuppressed
        );
        Assert.Equal(
            1,
            report.FindingsCount
        );
        Assert.Equal(
            0,
            report.UnknownRowsCount
        );
        Assert.True(
            report.IsFullyAccountedFor
        );
    }

    private static AcronisDeviceResourceDto Device(
        string id,
        string name,
        string state)
    {
        var backupTime =
            DateTimeOffset.Parse(
                "2026-10-08T12:00:00Z"
            );

        return new AcronisDeviceResourceDto
        {
            Id = id,
            Name = name,
            Status =
                new AcronisDeviceResourceStatusDto
                {
                    State = state,
                    LastBackup = backupTime,
                    LastSuccessBackup = backupTime,
                    AppliedPolicyNames =
                        state == "notProtected"
                            ? "SQL Database Backup (Disabled)"
                            : "Server Backup"
                }
        };
    }

    private sealed class StubTenantProvider
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TenantDto> tenants =
            [
                new TenantDto
                {
                    Id =
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    Name =
                        "Customer Alpha"
                }
            ];

            return Task.FromResult(
                tenants
            );
        }
    }

    private sealed class StubMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        private readonly IReadOnlyList<AcronisMicrosoft365ResourceDto>
            _resources;

        public StubMicrosoft365Provider(
            IReadOnlyList<AcronisMicrosoft365ResourceDto> resources)
        {
            _resources = resources;
        }

        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _resources
            );
        }
    }

    private sealed class StubDeviceProvider
        : IAcronisDeviceInventoryProvider
    {
        private readonly IReadOnlyList<AcronisDeviceResourceDto>
            _resources;

        public StubDeviceProvider(
            IReadOnlyList<AcronisDeviceResourceDto> resources)
        {
            _resources = resources;
        }

        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _resources
            );
        }
    }
}
