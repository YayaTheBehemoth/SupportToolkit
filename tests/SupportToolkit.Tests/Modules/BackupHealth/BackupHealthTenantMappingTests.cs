using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Tests;

public class BackupHealthTenantMappingTests
{
    [Fact]
    public async Task NumericTenantId_IsMappedToV2TenantUuid()
    {
        var provider =
            new MappingProvider();

        var service =
            new BackupHealthService(
                provider
            );

        var snapshot =
            await service
                .GetSnapshotAsync();

        var resource =
            Assert.Single(
                snapshot.Resources
            );

        Assert.Equal(
            "11111111-1111-1111-1111-111111111111",
            resource.TenantId
        );

        Assert.Equal(
            "Customer Alpha",
            resource.TenantName
        );

        Assert.Empty(
            snapshot.Diagnostics
        );

        Assert.Equal(
            1,
            provider.MappingRequestCount
        );
    }

    private sealed class MappingProvider
        : IAcronisProvider,
          IAcronisTenantMappingProvider
    {
        public int MappingRequestCount
        {
            get;
            private set;
        }

        public Task<IReadOnlyList<TenantDto>>
            GetTenantsAsync(
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<TenantDto> tenants =
            [
                new TenantDto
                {
                    Id =
                        "11111111-1111-1111-1111-111111111111",

                    Name =
                        "Customer Alpha"
                }
            ];

            return Task.FromResult(
                tenants
            );
        }

        public Task<IReadOnlyList<ResourceStatusDto>>
            GetResourceStatusesAsync(
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ResourceStatusDto> resources =
            [
                new ResourceStatusDto
                {
                    Context =
                        new ResourceDto
                        {
                            Id =
                                "resource-001",

                            Name =
                                "SERVER-01",

                            TenantId =
                                "1001",

                            Type =
                                "resource.machine"
                        },

                    Aggregate =
                        new AggregateStatusDto
                        {
                            Status =
                                "idle"
                        },

                    Policies =
                        []
                }
            ];

            return Task.FromResult(
                resources
            );
        }

        public Task<IReadOnlyList<AlertDto>>
            GetAlertsAsync(
                CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AlertDto> alerts =
                [];

            return Task.FromResult(
                alerts
            );
        }

        public Task<IReadOnlyDictionary<string, string>>
            GetTenantIdMappingsAsync(
                CancellationToken cancellationToken = default)
        {
            MappingRequestCount++;

            IReadOnlyDictionary<string, string> mappings =
                new Dictionary<string, string>
                {
                    ["1001"] =
                        "11111111-1111-1111-1111-111111111111"
                };

            return Task.FromResult(
                mappings
            );
        }
    }
}