using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Tenants;

public sealed class AcronisTenantProvider
    : IAcronisTenantProvider,
      IAcronisTenantMappingProvider
{
    private const int PageSize = 100;
    private const int MaxHierarchyNodes = 10_000;

    private readonly AcronisApiClient _apiClient;
    private readonly AcronisCursorPaginator _paginator;
    private readonly OperationalLogger? _logger;

    private IReadOnlyDictionary<string, string>?
        _tenantIdMappings;

    public AcronisTenantProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient = apiClient;

        _paginator =
            new AcronisCursorPaginator(
                apiClient,
                logger
            );

        _logger = logger;
    }

    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Fetching Acronis tenants."
        );

        var rootTenantId =
            await _apiClient.GetRootTenantIdAsync(
                cancellationToken
            );

        var tenants =
            await _paginator.FetchAllAsync<
                TenantPageDto,
                TenantDto>(
                datasetName:
                    "tenants",
                firstRequestPath:
                    "/api/2/tenants" +
                    $"?subtree_root_id={Uri.EscapeDataString(rootTenantId)}" +
                    "&lod=basic" +
                    $"&limit={PageSize}",
                buildNextRequestPath:
                    after =>
                        "/api/2/tenants" +
                        $"?limit={PageSize}" +
                        $"&after={Uri.EscapeDataString(after)}",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    page => page.Paging.Cursors.After,
                cancellationToken:
                    cancellationToken
            );

        if (tenants.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero tenants. " +
                "Verify API-client scope and tenant discovery."
            );
        }

        _logger?.Info(
            $"Acronis tenants fetched: {tenants.Count}."
        );

        return tenants;
    }

    public async Task<IReadOnlyDictionary<string, string>>
        GetTenantIdMappingsAsync(
            CancellationToken cancellationToken = default)
    {
        if (_tenantIdMappings is not null)
        {
            _logger?.Debug(
                "Using cached Acronis tenant ID mapping."
            );

            return _tenantIdMappings;
        }

        var rootUuid =
            await _apiClient.GetRootTenantIdAsync(
                cancellationToken
            );

        using var rootResponse =
            await _apiClient.GetAsync(
                $"/api/1/groups/{Uri.EscapeDataString(rootUuid)}",
                cancellationToken
            );

        rootResponse.EnsureSuccessStatusCode();

        var root =
            await rootResponse.Content
                .ReadFromJsonAsync<LegacyTenantGroupDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty legacy root tenant response."
            );

        ValidateLegacyGroup(
            root
        );

        var mappings =
            new Dictionary<string, string>(
                StringComparer.Ordinal
            );

        AddMapping(
            mappings,
            root.Id,
            root.Uuid!
        );

        var queue =
            new Queue<long>();

        var visited =
            new HashSet<long>();

        queue.Enqueue(
            root.Id
        );

        while (queue.Count > 0)
        {
            var currentNumericId =
                queue.Dequeue();

            if (!visited.Add(currentNumericId))
            {
                continue;
            }

            if (visited.Count > MaxHierarchyNodes)
            {
                throw new InvalidOperationException(
                    "Acronis tenant hierarchy traversal exceeded " +
                    $"{MaxHierarchyNodes} branch nodes."
                );
            }

            using var response =
                await _apiClient.GetAsync(
                    $"/api/1/groups/" +
                    $"{currentNumericId.ToString(CultureInfo.InvariantCulture)}" +
                    "/children",
                    cancellationToken
                );

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<LegacyTenantChildrenPageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty tenant hierarchy response."
                );

            foreach (var child in page.Items)
            {
                if (child.Id <= 0
                    || string.IsNullOrWhiteSpace(child.Uuid)
                    || !Guid.TryParse(child.Uuid, out _))
                {
                    _logger?.Warning(
                        "Acronis returned an invalid tenant hierarchy item."
                    );

                    continue;
                }

                AddMapping(
                    mappings,
                    child.Id,
                    child.Uuid
                );

                if (child.HasChildren > 0)
                {
                    queue.Enqueue(
                        child.Id
                    );
                }
            }
        }

        _tenantIdMappings =
            new ReadOnlyDictionary<string, string>(
                mappings
            );

        _logger?.Debug(
            $"Acronis tenant hierarchy mapping completed: " +
            $"{mappings.Count} mapping(s)."
        );

        return _tenantIdMappings;
    }

    private static void ValidateLegacyGroup(
        LegacyTenantGroupDto group)
    {
        if (group.Id <= 0)
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid legacy root tenant ID."
            );
        }

        if (string.IsNullOrWhiteSpace(group.Uuid)
            || !Guid.TryParse(group.Uuid, out _))
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid root tenant UUID."
            );
        }
    }

    private static void AddMapping(
        IDictionary<string, string> mappings,
        long numericId,
        string uuid)
    {
        var key =
            numericId.ToString(
                CultureInfo.InvariantCulture
            );

        if (mappings.TryGetValue(
                key,
                out var existingUuid
            )
            && !string.Equals(
                existingUuid,
                uuid,
                StringComparison.OrdinalIgnoreCase
            ))
        {
            throw new InvalidOperationException(
                "Acronis returned conflicting UUID mappings " +
                "for the same legacy tenant ID."
            );
        }

        mappings[key] = uuid;
    }
}
