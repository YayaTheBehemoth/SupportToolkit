using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Epm.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Epm;

public sealed class AcronisEpmResourceProvider
{
    private const int PageSize = 30;
    private const int MaxPages = 500;
    private const int MaxResources = 25_000;

    /*
     * Acronis frontend constant:
     *
     *   AllMachinesKey = "all"
     *   AllMachinesId  = "f656db8d-3b82-40ba-b06a-bffc9b0b825a"
     *
     * This is the built-in virtual group backing the
     * "All devices" / "Alle enheder" inventory view.
     *
     * It is an Acronis product constant, not a tenant-specific UUID.
     */
    private const string AllMachinesGroupId =
        "f656db8d-3b82-40ba-b06a-bffc9b0b825a";

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisEpmResourceProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AcronisEpmResourceDto>>
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

        var scopedToken =
            await _apiClient.GetScopedAccessTokenAsync(
                tenantId,
                cancellationToken
            );

        _logger?.Info(
            "Fetching canonical EPM 'All devices' inventory."
        );

        var resources =
            await GetAllMachinesResourcesAsync(
                scopedToken,
                cancellationToken
            );

        var unique =
            new Dictionary<string, AcronisEpmResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var resourcesWithoutId =
            new List<AcronisEpmResourceDto>();

        foreach (var resource in resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Id))
            {
                /*
                 * Do not silently discard objects that cannot be
                 * deduplicated. They must remain in inventory so the
                 * normalizer can surface them as unknown if necessary.
                 */
                resourcesWithoutId.Add(resource);
                continue;
            }

            unique[resource.Id] = resource;
        }

        var result =
            unique.Values
                .Concat(resourcesWithoutId)
                .ToList()
                .AsReadOnly();

        _logger?.Info(
            $"Unique EPM 'All devices' resources fetched: {result.Count}."
        );

        return result;
    }

    private async Task<IReadOnlyList<AcronisEpmResourceDto>>
        GetAllMachinesResourcesAsync(
            string scopedToken,
            CancellationToken cancellationToken)
    {
        var resources =
            new List<AcronisEpmResourceDto>();

        string? requestPath =
            BuildInitialResourcePath();

        var pageNumber = 1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"EPM resource pagination exceeded {MaxPages} pages."
                );
            }

            _logger?.Info(
                $"Fetching EPM 'All devices' page {pageNumber}."
            );

            using var response =
                await _apiClient.GetWithAccessTokenAsync(
                    requestPath,
                    scopedToken,
                    cancellationToken
                );

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<AcronisEpmResourcePageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty EPM resource response."
                );

            resources.AddRange(page.Items);

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"EPM resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Info(
                $"EPM 'All devices' page {pageNumber}: " +
                $"{page.Items.Count} item(s)."
            );

            var after =
                page.Paging.Cursors.After;

            if (string.IsNullOrWhiteSpace(after))
            {
                requestPath = null;
                continue;
            }

            requestPath =
                BuildNextResourcePath(after);

            pageNumber++;
        }

        return resources.AsReadOnly();
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