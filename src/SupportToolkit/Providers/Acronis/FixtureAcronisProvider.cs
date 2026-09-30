using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

public sealed class FixtureAcronisProvider : IAcronisProvider
{
    private readonly string _resourceStatusesPath;
    private readonly string _tenantsPath;

    public FixtureAcronisProvider(
        string resourceStatusesPath,
        string tenantsPath)
    {
        _resourceStatusesPath = resourceStatusesPath;
        _tenantsPath = tenantsPath;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(
            _resourceStatusesPath,
            cancellationToken
        );

        var page =
            JsonSerializer.Deserialize<ResourceStatusPageDto>(json)
            ?? throw new InvalidOperationException(
                "Unable to deserialize Acronis resource status fixture."
            );

        return page.Items;
    }

    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(
            _tenantsPath,
            cancellationToken
        );

        var page =
            JsonSerializer.Deserialize<TenantPageDto>(json)
            ?? throw new InvalidOperationException(
                "Unable to deserialize Acronis tenant fixture."
            );

        return page.Items;
    }
}