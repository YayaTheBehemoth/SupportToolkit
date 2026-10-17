
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
        _apiClient =
            apiClient ?? throw new ArgumentNullException(nameof(apiClient));

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

        /*
         * Continuation cursors are opaque and case-sensitive.
         *
         * A repeated cursor indicates pagination is not making
         * forward progress. Fail the review instead of returning
         * incomplete inventory.
         *
         * Never include actual cursor values in diagnostics.
         */
        var seenAfterCursors =
            new HashSet<string>(StringComparer.Ordinal);

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
                        cancellationToken: cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty device resource response."
                );

            /*
             * Missing or null collections are incomplete responses,
             * not successful empty inventories.
             *
             * An explicitly empty array remains valid.
             */
            if (!page.HasItemsProperty)
            {
                throw new InvalidOperationException(
                    "Acronis device resource response did not include " +
                    "an 'items' collection."
                );
            }

            var pageItems = page.Items;

            if (pageItems is null)
            {
                throw new InvalidOperationException(
                    "Acronis device resource response contained " +
                    "a null 'items' collection."
                );
            }

            resources.AddRange(pageItems);

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"Device resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Debug(
                $"Device resource page {pageNumber}: " +
                $"{pageItems.Count} item(s)."
            );

            /*
             * Preserve existing terminal-page behavior.
             *
             * Multi-page device continuation remains fixture-tested,
             * not production-verified.
             */
            var after =
                page.Paging.Cursors.After;

            if (string.IsNullOrWhiteSpace(after))
            {
                requestPath = null;
            }
            else
            {
                /*
                 * Detect immediate repetitions and longer cursor
                 * cycles such as A -> B -> A.
                 */
                if (!seenAfterCursors.Add(after))
                {
                    throw new InvalidOperationException(
                        "Acronis device resource pagination " +
                        "returned a repeated continuation cursor."
                    );
                }

                /*
                 * Preserve the existing after-only continuation query.
                 */
                requestPath =
                    BuildNextResourcePath(after);
            }

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
