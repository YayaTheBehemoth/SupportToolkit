using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Devices;

/// <summary>
/// Reads the endpoint/device inventory shown by Acronis "All devices".
/// </summary>
public sealed class AcronisDeviceInventoryProvider
    : IAcronisDeviceInventoryProvider
{
    private const int PageSize = 30;
    private const int MaxPages = 500;
    private const int MaxResources = 25_000;

    /*
     * Built-in Acronis virtual group backing "All devices".
     * This identifier is a product constant, not tenant-specific data.
     */
    internal const string AllMachinesGroupId =
        "f656db8d-3b82-40ba-b06a-bffc9b0b825a";

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisDeviceInventoryProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AcronisDeviceResourceDto>>
        GetResourcesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(tenantId, out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for customer-scoped authentication.",
                nameof(tenantId)
            );
        }

        var scopedAccessToken =
            await _apiClient.GetScopedAccessTokenAsync(
                tenantId,
                cancellationToken
            );

        var rawResources =
            await GetAllDevicesAsync(
                scopedAccessToken,
                cancellationToken
            );

        var uniqueResources =
            Deduplicate(rawResources);

        _logger?.Debug(
            $"Device inventory rows: {rawResources.Count} raw, " +
            $"{uniqueResources.Count} unique."
        );

        return uniqueResources;
    }

    private async Task<IReadOnlyList<AcronisDeviceResourceDto>>
        GetAllDevicesAsync(
            string scopedAccessToken,
            CancellationToken cancellationToken)
    {
        var resources =
            new List<AcronisDeviceResourceDto>();

        string? requestPath =
            BuildInitialResourcePath();

        var pageNumber = 1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Device resource pagination exceeded {MaxPages} pages."
                );
            }

            using var response =
                await _apiClient.GetWithAccessTokenAsync(
                    requestPath,
                    scopedAccessToken,
                    cancellationToken
                );

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<AcronisDeviceResourcePageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty device resource response."
                );

            resources.AddRange(page.Items);

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"Device resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Debug(
                $"Device resource page {pageNumber}: " +
                $"{page.Items.Count} item(s)."
            );

            var after =
                page.Paging.Cursors.After;

            requestPath =
                string.IsNullOrWhiteSpace(after)
                    ? null
                    : BuildNextResourcePath(after);

            pageNumber++;
        }

        return resources.AsReadOnly();
    }

    private static IReadOnlyList<AcronisDeviceResourceDto>
        Deduplicate(
            IEnumerable<AcronisDeviceResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisDeviceResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisDeviceResourceDto>();

        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                withoutIdentity.Add(resource);
                continue;
            }

            unique[resource.Id] = resource;
        }

        return unique.Values
            .Concat(withoutIdentity)
            .ToList()
            .AsReadOnly();
    }

    private static string BuildInitialResourcePath()
    {
        return
            "/bc/api/resource_manager/v1/epm/resources" +
            "?checkCredentials=1" +
            "&embed=details" +
            "&embed=agent" +
            $"&limit={PageSize}" +
            "&order=asc(name)" +
            $"&parentId={AllMachinesGroupId}" +
            "&timestamp=1";
    }

    private static string BuildNextResourcePath(
        string after)
    {
        return
            "/bc/api/resource_manager/v1/epm/resources" +
            $"?after={Uri.EscapeDataString(after)}";
    }
}
