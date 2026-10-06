using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Development and test implementation of the Acronis provider boundary.
///
/// Fixture mode serves production-shaped synthetic Acronis responses from
/// local files so SupportToolkit can be developed independently of real
/// credentials, network access, and customer data.
///
/// Tenant-ID mapping is represented by a synthetic precomputed fixture rather
/// than reproducing HTTP hierarchy traversal. The traversal implementation is
/// tested separately by the production provider tests.
/// </summary>
public sealed class FixtureAcronisProvider
    : IAcronisProvider,
      IAcronisTenantMappingProvider
{
    private readonly string _fixtureDirectory;

    public FixtureAcronisProvider(
        string fixtureDirectory)
    {
        _fixtureDirectory =
            fixtureDirectory;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        var page =
            await ReadFixtureAsync<ResourceStatusPageDto>(
                "resource-statuses.json",
                cancellationToken
            );

        return page.Items;
    }

    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        var page =
            await ReadFixtureAsync<TenantPageDto>(
                "tenants.json",
                cancellationToken
            );

        return page.Items;
    }

    public async Task<IReadOnlyList<AlertDto>>
        GetAlertsAsync(
            CancellationToken cancellationToken = default)
    {
        var page =
            await ReadFixtureAsync<AlertPageDto>(
                "alerts.json",
                cancellationToken
            );

        return page.Items;
    }

    public async Task<IReadOnlyDictionary<string, string>>
        GetTenantIdMappingsAsync(
            CancellationToken cancellationToken = default)
    {
        var mappings =
            await ReadFixtureAsync<
                Dictionary<string, string>>(
                "tenant-id-mappings.json",
                cancellationToken
            );

        return mappings;
    }

    private async Task<T> ReadFixtureAsync<T>(
        string fileName,
        CancellationToken cancellationToken)
    {
        var path =
            Path.Combine(
                _fixtureDirectory,
                fileName
            );

        var json =
            await File.ReadAllTextAsync(
                path,
                cancellationToken
            );

        return JsonSerializer.Deserialize<T>(
                   json
               )
               ?? throw new InvalidOperationException(
                   $"Unable to deserialize Acronis fixture " +
                   $"'{fileName}'."
               );
    }
}