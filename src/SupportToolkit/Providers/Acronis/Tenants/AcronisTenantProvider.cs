
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
        _apiClient =
            apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        _paginator =
            new AcronisCursorPaginator(
                apiClient,
                logger);

        _logger = logger;
    }

    public Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        return GetTenantsAsync("basic", cancellationToken);
    }

    /// <summary>
    /// Same read-only listing at an explicit level of detail. Used by
    /// diagnostics only; operational callers use the basic listing.
    /// </summary>
    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            string levelOfDetail,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(levelOfDetail);

        _logger?.Info(
            "Fetching Acronis tenants.");

        var rootTenantId =
            await _apiClient.GetRootTenantIdAsync(
                cancellationToken);

        var tenants =
            await _paginator.FetchAllAsync<
                TenantPageDto,
                TenantDto>(
                datasetName:
                    "tenants",
                firstRequestPath:
                    "/api/2/tenants" +
                    $"?subtree_root_id={Uri.EscapeDataString(rootTenantId)}" +
                    $"&lod={Uri.EscapeDataString(levelOfDetail)}" +
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
                    cancellationToken);

        if (tenants.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero tenants. " +
                "Verify API-client scope and tenant discovery.");
        }

        _logger?.Info(
            $"Acronis tenants fetched: {tenants.Count}.");

        return tenants;
    }

    public async Task<IReadOnlyDictionary<string, string>>
        GetTenantIdMappingsAsync(
            CancellationToken cancellationToken = default)
    {
        if (_tenantIdMappings is not null)
        {
            _logger?.Debug(
                "Using cached Acronis tenant ID mapping.");

            return _tenantIdMappings;
        }

        var rootUuid =
            await _apiClient.GetRootTenantIdAsync(
                cancellationToken);

        using var rootResponse =
            await _apiClient.GetAsync(
                $"/api/1/groups/{Uri.EscapeDataString(rootUuid)}",
                cancellationToken);

        rootResponse.EnsureSuccessStatusCode();

        var root =
            await rootResponse.Content
                .ReadFromJsonAsync<LegacyTenantGroupDto>(
                    cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "Acronis returned an empty legacy root tenant response.");

        ValidateLegacyGroup(root);

        var mappings =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        AddMapping(
            mappings,
            root.Id,
            root.Uuid!);

        var queue =
            new Queue<long>();

        var visited =
            new HashSet<long>();

        queue.Enqueue(root.Id);

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
                    $"{MaxHierarchyNodes} branch nodes.");
            }

            using var response =
                await _apiClient.GetAsync(
                    "/api/1/groups/" +
                    currentNumericId.ToString(
                        CultureInfo.InvariantCulture) +
                    "/children",
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<LegacyTenantChildrenPageDto>(
                        cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty tenant hierarchy response.");

            /*
             * 'items' is already required by the DTO.
             *
             * Missing property: JSON deserialization fails.
             * Explicit null: reject here.
             * Empty array: valid terminal response.
             */
            if (page.Items is null)
            {
                throw new InvalidOperationException(
                    "Acronis tenant hierarchy response contained " +
                    "a null 'items' collection. " +
                    "Tenant ID mapping is incomplete.");
            }

            foreach (var child in page.Items)
            {
                if (child is null)
                {
                    throw new InvalidOperationException(
                        "Acronis returned a null tenant hierarchy item. " +
                        "Tenant ID mapping is incomplete.");
                }

                /*
                 * Invalid identifiers must not be skipped.
                 *
                 * Skipping an entry could silently omit a tenant
                 * or its descendants from the mapping.
                 */
                if (child.Id <= 0
                    || string.IsNullOrWhiteSpace(child.Uuid)
                    || !Guid.TryParse(child.Uuid, out _))
                {
                    throw new InvalidOperationException(
                        "Acronis returned an invalid tenant hierarchy item. " +
                        "Tenant ID mapping is incomplete.");
                }

                /*
                 * Presence and JSON-type validation are handled
                 * by the DTO's required integer property.
                 *
                 * Do not assume values are strictly limited to 0/1.
                 * Any positive value continues traversal.
                 */
                if (child.HasChildren < 0)
                {
                    throw new InvalidOperationException(
                        "Acronis returned an invalid 'has_children' value. " +
                        "Tenant ID mapping is incomplete.");
                }

                AddMapping(
                    mappings,
                    child.Id,
                    child.Uuid);

                if (child.HasChildren > 0)
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        /*
         * Only publish/cache the mapping after the entire
         * traversal has completed successfully.
         */
        _tenantIdMappings =
            new ReadOnlyDictionary<string, string>(
                mappings);

        _logger?.Debug(
            $"Acronis tenant hierarchy mapping completed: " +
            $"{mappings.Count} mapping(s).");

        return _tenantIdMappings;
    }

    private static void ValidateLegacyGroup(
        LegacyTenantGroupDto group)
    {
        if (group.Id <= 0)
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid legacy root tenant ID.");
        }

        if (string.IsNullOrWhiteSpace(group.Uuid)
            || !Guid.TryParse(group.Uuid, out _))
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid root tenant UUID.");
        }
    }

    private static void AddMapping(
        IDictionary<string, string> mappings,
        long numericId,
        string uuid)
    {
        var key =
            numericId.ToString(
                CultureInfo.InvariantCulture);

        if (mappings.TryGetValue(
                key,
                out var existingUuid)
            && !string.Equals(
                existingUuid,
                uuid,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Acronis returned conflicting UUID mappings " +
                "for the same legacy tenant ID.");
        }

        mappings[key] = uuid;
    }
}
