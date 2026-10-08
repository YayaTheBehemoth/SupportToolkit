using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class AcronisTenantResolverTests
{
    [Fact]
    public async Task ResolveExactAsync_MatchesCaseInsensitively()
    {
        var resolver =
            new AcronisTenantResolver(
                new StubTenantProvider(
                    [
                        Tenant(
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                            "Customer Alpha"
                        )
                    ]
                )
            );

        var tenant =
            await resolver.ResolveExactAsync(
                "customer alpha"
            );

        Assert.Equal(
            "Customer Alpha",
            tenant.Name
        );
    }

    [Fact]
    public async Task ResolveExactAsync_NoMatch_Throws()
    {
        var resolver =
            new AcronisTenantResolver(
                new StubTenantProvider([])
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
                () =>
                    resolver.ResolveExactAsync(
                        "Missing"
                    )
            );
    }

    [Fact]
    public async Task ResolveExactAsync_AmbiguousMatch_Throws()
    {
        var resolver =
            new AcronisTenantResolver(
                new StubTenantProvider(
                    [
                        Tenant(
                            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                            "Duplicate"
                        ),
                        Tenant(
                            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                            "Duplicate"
                        )
                    ]
                )
            );

        await Assert.ThrowsAsync<
            InvalidOperationException>(
                () =>
                    resolver.ResolveExactAsync(
                        "Duplicate"
                    )
            );
    }

    private static TenantDto Tenant(
        string id,
        string name)
    {
        return new TenantDto
        {
            Id = id,
            Name = name
        };
    }

    private sealed class StubTenantProvider
        : IAcronisTenantProvider
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
            return Task.FromResult(
                _tenants
            );
        }
    }
}
