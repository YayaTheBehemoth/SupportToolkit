using System.Text.Json;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;


/// Development and test implementation of "IAcronisProvider" that serves
/// production-shaped synthetic Acronis responses from local fixture files.
/// This allows the rest of SupportToolkit to be developed and exercised independently of
/// real credentials, API availability, or network access.

public sealed class FixtureAcronisProvider : IAcronisProvider
{
    private readonly string _fixtureDirectory;


    public FixtureAcronisProvider(string fixtureDirectory)
    {
        _fixtureDirectory = fixtureDirectory;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        var page = await ReadFixtureAsync<ResourceStatusPageDto>(
            "resource-statuses.json",
            cancellationToken
        );

        return page.Items;
    }


    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        var page = await ReadFixtureAsync<TenantPageDto>(
            "tenants.json",
            cancellationToken
        );

        return page.Items;
    }

    public async Task<IReadOnlyList<AlertDto>>
        GetAlertsAsync(
            CancellationToken cancellationToken = default)
    {
        var page = await ReadFixtureAsync<AlertPageDto>(
            "alerts.json",
            cancellationToken
        );

        return page.Items;
    }


    private async Task<T> ReadFixtureAsync<T>(
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            _fixtureDirectory,
            fileName
        );

        var json = await File.ReadAllTextAsync(
            path,
            cancellationToken
        );

        return JsonSerializer.Deserialize<T>(json)
            ?? throw new InvalidOperationException(
                $"Unable to deserialize Acronis fixture '{fileName}'."
            );
    }
}