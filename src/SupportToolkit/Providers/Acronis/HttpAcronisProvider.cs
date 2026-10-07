using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Alerts.Dtos;
using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;
namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Production Acronis provider backed by the Acronis HTTP APIs.
///
/// Transport concerns such as endpoint paths, pagination, legacy identifier
/// compatibility, hierarchy traversal, production-shape diagnostics, and
/// response deserialization are contained here so consuming modules remain
/// isolated from Acronis transport details.
/// </summary>
public sealed class HttpAcronisProvider
    : IAcronisProvider,
      IAcronisTenantMappingProvider
{
    private const int PageSize = 100;

    /*
     * Defensive upper bound for hierarchy traversal.
     *
     * The Acronis tenant hierarchy should be finite, but external traversal
     * code should never be capable of making unbounded requests if vendor
     * data is malformed.
     */
    private const int MaxHierarchyNodes = 10_000;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    private IReadOnlyDictionary<string, string>?
        _tenantIdMappings;

    public HttpAcronisProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient;

        _logger =
            logger;
    }

    public async Task<IReadOnlyList<TenantDto>>
        GetTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Fetching Acronis tenants."
        );

        var rootTenantId =
            await _apiClient
                .GetRootTenantIdAsync(
                    cancellationToken
                );

        var firstRequestPath =
            "/api/2/tenants" +
            $"?subtree_root_id=" +
            $"{Uri.EscapeDataString(rootTenantId)}" +
            "&lod=basic" +
            $"&limit={PageSize}";

        var tenants =
            await FetchAllPagesAsync<
                TenantPageDto,
                TenantDto>(
                datasetName:
                    "tenants",
                firstRequestPath:
                    firstRequestPath,
                endpointPath:
                    "/api/2/tenants",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    page =>
                        page.Paging.Cursors.After,
                cancellationToken:
                    cancellationToken
            );

        _logger?.Info(
            $"Acronis tenants fetched: " +
            $"{tenants.Count}."
        );

        if (tenants.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero tenants. " +
                "Verify API-client scope and tenant discovery."
            );
        }
        else
        {
            LogTenantShapeSummary(
                tenants
            );
        }

        return tenants;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Fetch the complete resource-status surface.
         *
         * resource.group.* filtering belongs in consuming module normalization,
         * not transport, because different modules may legitimately need those
         * structural objects.
         */
        _logger?.Info(
            "Fetching Acronis resource statuses."
        );

        var firstRequestPath =
            "/api/resource_management/v4/resource_statuses" +
            $"?limit={PageSize}";

        var resources =
            await FetchAllPagesAsync<
                ResourceStatusPageDto,
                ResourceStatusDto>(
                datasetName:
                    "resource statuses",
                firstRequestPath:
                    firstRequestPath,
                endpointPath:
                    "/api/resource_management/v4/resource_statuses",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    page =>
                        page.Paging.Cursors.After,
                cancellationToken:
                    cancellationToken
            );

        _logger?.Info(
            $"Acronis resource statuses fetched: " +
            $"{resources.Count}."
        );

        if (resources.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero resource statuses. " +
                "Verify API-client scope and resource-status visibility."
            );
        }
        else
        {
            LogResourceShapeSummary(
                resources
            );
        }

        return resources;
    }

    /// <summary>
    /// Fetches the complete resource-status surface visible from one
    /// customer-scoped Acronis access token.
    ///
    /// The caller supplies the Account Management tenant UUID. Token exchange,
    /// authentication, pagination, and transport concerns remain inside the
    /// Acronis provider layer rather than leaking into consuming modules.
    /// </summary>
    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(
                tenantId,
                out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for a " +
                "customer-scoped resource-status request.",
                nameof(tenantId)
            );
        }

        _logger?.Info(
            "Fetching customer-scoped Acronis resource statuses."
        );

        var scopedAccessToken =
            await _apiClient
                .GetScopedAccessTokenAsync(
                    tenantId,
                    cancellationToken
                );

        var firstRequestPath =
            "/api/resource_management/v4/resource_statuses" +
            $"?limit={PageSize}";

        var resources =
            await FetchAllPagesAsync<
                ResourceStatusPageDto,
                ResourceStatusDto>(
                datasetName:
                    "customer-scoped resource statuses",
                firstRequestPath:
                    firstRequestPath,
                endpointPath:
                    "/api/resource_management/v4/resource_statuses",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    page =>
                        page.Paging.Cursors.After,
                cancellationToken:
                    cancellationToken,
                accessToken:
                    scopedAccessToken
            );

        _logger?.Info(
            $"Customer-scoped Acronis resource statuses fetched: " +
            $"{resources.Count}."
        );

        if (resources.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero customer-scoped resource statuses."
            );
        }
        else
        {
            LogResourceShapeSummary(
                resources
            );
        }

        return resources;
    }

    public async Task<IReadOnlyList<AlertDto>>
        GetAlertsAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Zero active alerts is valid and therefore does not produce a
         * warning.
         */
        _logger?.Info(
            "Fetching active Acronis alerts."
        );

        var firstRequestPath =
            "/api/alert_manager/v1/alerts" +
            $"?show_deleted=false" +
            $"&limit={PageSize}";

        var alerts =
            await FetchAllPagesAsync<
                AlertPageDto,
                AlertDto>(
                datasetName:
                    "alerts",
                firstRequestPath:
                    firstRequestPath,
                endpointPath:
                    "/api/alert_manager/v1/alerts",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    GetAlertAfterCursor,
                cancellationToken:
                    cancellationToken
            );

        _logger?.Info(
            $"Active Acronis alerts fetched: " +
            $"{alerts.Count}."
        );

        return alerts;
    }

    /// <summary>
    /// Builds the complete accessible mapping between legacy numeric Acronis
    /// tenant identifiers and Account Management v2 UUIDs.
    ///
    /// The root UUID is converted exactly once through the legacy API.
    /// Hierarchy traversal then uses only the numeric IDs returned by Acronis.
    ///
    /// The completed map is cached for the lifetime of this provider instance.
    /// </summary>
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

        _logger?.Info(
            "Building Acronis tenant ID mapping from hierarchy."
        );

        var rootUuid =
            await _apiClient
                .GetRootTenantIdAsync(
                    cancellationToken
                );

        /*
         * Bootstrap Account Management v1.
         *
         * The v2 client metadata endpoint gives us a UUID. The legacy
         * hierarchy API exposes the corresponding numeric ID that older
         * Acronis API surfaces still use.
         *
         * IMPORTANT:
         * Account Management API v1 uses /groups/, plural.
         */
        using var rootResponse =
            await _apiClient.GetAsync(
                $"/api/1/groups/" +
                $"{Uri.EscapeDataString(rootUuid)}",
                cancellationToken
            );

        rootResponse.EnsureSuccessStatusCode();

        var root =
            await rootResponse.Content
                .ReadFromJsonAsync<
                    LegacyTenantGroupDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty legacy root tenant response."
            );

        if (root.Id <= 0)
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid legacy root tenant ID."
            );
        }

        if (string.IsNullOrWhiteSpace(
                root.Uuid)
            || !Guid.TryParse(
                root.Uuid,
                out _))
        {
            throw new InvalidOperationException(
                "Acronis returned an invalid root tenant UUID."
            );
        }

        var mappings =
            new Dictionary<string, string>(
                StringComparer.Ordinal
            );

        AddMapping(
            mappings,
            root.Id,
            root.Uuid
        );

        /*
         * Queue and visited-set deliberately contain numeric IDs.
         *
         * Production validation showed that /children does not accept the
         * root UUID in this environment.
         */
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

            if (!visited.Add(
                    currentNumericId))
            {
                continue;
            }

            if (visited.Count
                > MaxHierarchyNodes)
            {
                throw new InvalidOperationException(
                    $"Acronis tenant hierarchy traversal exceeded " +
                    $"{MaxHierarchyNodes} branch nodes. The run was " +
                    "aborted to prevent unbounded API traversal."
                );
            }

            _logger?.Debug(
                $"Fetching tenant hierarchy children for branch " +
                $"{visited.Count}."
            );

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
                    .ReadFromJsonAsync<
                        LegacyTenantChildrenPageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty tenant hierarchy response."
                );

            foreach (var child
                     in page.Items)
            {
                if (child.Id <= 0)
                {
                    _logger?.Warning(
                        "Acronis returned a hierarchy item with an " +
                        "invalid legacy tenant ID."
                    );

                    continue;
                }

                if (string.IsNullOrWhiteSpace(
                        child.Uuid)
                    || !Guid.TryParse(
                        child.Uuid,
                        out _))
                {
                    _logger?.Warning(
                        "Acronis returned a hierarchy item with an " +
                        "invalid tenant UUID."
                    );

                    continue;
                }

                AddMapping(
                    mappings,
                    child.Id,
                    child.Uuid
                );

                /*
                 * Leaf tenants are already fully mapped by the current
                 * response. Only branch nodes require another API request.
                 */
                if (child.HasChildren > 0)
                {
                    queue.Enqueue(
                        child.Id
                    );
                }
            }
        }

        _tenantIdMappings =
            new ReadOnlyDictionary<
                string,
                string>(
                mappings
            );

        _logger?.Info(
            $"Acronis tenant hierarchy mapping completed: " +
            $"{mappings.Count} mapping(s) across " +
            $"{visited.Count} traversed branch node(s)."
        );

        return _tenantIdMappings;
    }

    /// <summary>
    /// Adds one legacy numeric-ID to UUID relationship while protecting
    /// against contradictory vendor data.
    /// </summary>
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
                out var existingUuid)
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

        mappings[key] =
            uuid;
    }

    /// <summary>
    /// Logs privacy-safe aggregate information about production tenant data.
    ///
    /// No tenant IDs, names, customer IDs, or unknown-field values are
    /// written to the console.
    /// </summary>
    private void LogTenantShapeSummary(
        IReadOnlyList<TenantDto> tenants)
    {
        if (_logger is null)
        {
            return;
        }

        _logger.Info(
            "Production tenant-shape summary:"
        );

        var tenantIdGroups =
            tenants
                .GroupBy(
                    tenant =>
                        ClassifyIdentifier(
                            tenant.Id
                        )
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var group
                 in tenantIdGroups)
        {
            _logger.Info(
                $"  tenant-id format '{group.Key}': " +
                $"{group.Count()}"
            );
        }

        var customerIdGroups =
            tenants
                .GroupBy(
                    tenant =>
                        ClassifyIdentifier(
                            tenant.CustomerId
                        )
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var group
                 in customerIdGroups)
        {
            _logger.Info(
                $"  customer-id format '{group.Key}': " +
                $"{group.Count()}"
            );
        }

        LogAdditionalTenantFields(
            tenants
        );
    }

    /// <summary>
    /// Summarizes unknown tenant properties by field name and JSON value kind.
    /// Values themselves are never logged.
    /// </summary>
    private void LogAdditionalTenantFields(
        IReadOnlyList<TenantDto> tenants)
    {
        if (_logger is null)
        {
            return;
        }

        var additionalFields =
            tenants
                .SelectMany(
                    tenant =>
                        tenant.AdditionalProperties
                            .Select(
                                property =>
                                    new
                                    {
                                        Name =
                                            property.Key,

                                        Kind =
                                            property.Value.ValueKind
                                    }
                            )
                )
                .GroupBy(
                    field =>
                        new
                        {
                            field.Name,
                            field.Kind
                        }
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key.Name,
                    StringComparer.OrdinalIgnoreCase
                )
                .ThenBy(
                    group =>
                        group.Key.Kind
                )
                .ToList();

        if (additionalFields.Count == 0)
        {
            _logger.Info(
                "  no additional tenant fields observed."
            );

            return;
        }

        _logger.Info(
            "  additional tenant fields observed:"
        );

        foreach (var group
                 in additionalFields)
        {
            _logger.Info(
                $"    '{group.Key.Name}' " +
                $"({group.Key.Kind}): " +
                $"{group.Count()}"
            );
        }
    }

    /// <summary>
    /// Logs privacy-safe aggregate information about the shape of production
    /// resource-status data.
    ///
    /// No resource names, resource IDs, tenant IDs, or payload values are
    /// emitted.
    /// </summary>
    private void LogResourceShapeSummary(
        IReadOnlyList<ResourceStatusDto> resources)
    {
        if (_logger is null)
        {
            return;
        }

        _logger.Info(
            "Production resource-shape summary:"
        );

        var typeGroups =
            resources
                .GroupBy(
                    resource =>
                        string.IsNullOrWhiteSpace(
                            resource.Context.Type
                        )
                            ? "<missing>"
                            : resource.Context.Type
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var group
                 in typeGroups)
        {
            _logger.Info(
                $"  resource type '{group.Key}': " +
                $"{group.Count()}"
            );
        }

        var tenantIdGroups =
            resources
                .GroupBy(
                    resource =>
                        ClassifyResourceTenantId(
                            resource.Context.TenantId
                        )
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .ThenBy(
                    group =>
                        group.Key,
                    StringComparer.OrdinalIgnoreCase
                );

        foreach (var group
                 in tenantIdGroups)
        {
            _logger.Info(
                $"  tenant-id format '{group.Key}': " +
                $"{group.Count()}"
            );
        }
    }

    private static string ClassifyResourceTenantId(
        string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(
                tenantId))
        {
            return "missing";
        }

        if (tenantId == "0")
        {
            return "zero";
        }

        return ClassifyIdentifier(
            tenantId
        );
    }

    private static string ClassifyIdentifier(
        string? identifier)
    {
        if (string.IsNullOrWhiteSpace(
                identifier))
        {
            return "missing";
        }

        if (Guid.TryParse(
                identifier,
                out _))
        {
            return "guid";
        }

        if (long.TryParse(
                identifier,
                out _))
        {
            return "numeric";
        }

        return "other";
    }

    /// <summary>
    /// Fetches every page for a cursor-paginated Acronis endpoint and returns
    /// one combined collection.
    ///
    /// When accessToken is supplied, every page in the sequence is fetched
    /// using that explicit token. This is required for customer-scoped API
    /// requests because pagination must remain inside the same authorization
    /// scope.
    /// </summary>
    private async Task<IReadOnlyList<TItem>>
        FetchAllPagesAsync<TPage, TItem>(
            string datasetName,
            string firstRequestPath,
            string endpointPath,
            Func<TPage, IEnumerable<TItem>> getItems,
            Func<TPage, string?> getAfterCursor,
            CancellationToken cancellationToken,
            string? accessToken = null)
        where TPage : class
    {
        var allItems =
            new List<TItem>();

        string? requestPath =
            firstRequestPath;

        var pageNumber =
            1;

        while (requestPath is not null)
        {
            _logger?.Info(
                $"Fetching {datasetName} page " +
                $"{pageNumber}."
            );

            var page =
                await GetPageAsync<TPage>(
                    requestPath,
                    cancellationToken,
                    accessToken
                );

            var pageItems =
                getItems(page)
                    .ToList();

            allItems.AddRange(
                pageItems
            );

            _logger?.Info(
                $"{datasetName} page " +
                $"{pageNumber}: " +
                $"{pageItems.Count} item(s)."
            );

            var after =
                getAfterCursor(page);

            if (string.IsNullOrWhiteSpace(
                    after))
            {
                requestPath =
                    null;

                continue;
            }

            /*
             * Cursors are opaque. Never inspect or reconstruct them.
             */
            requestPath =
                endpointPath +
                $"?limit={PageSize}" +
                $"&after=" +
                $"{Uri.EscapeDataString(after)}";

            pageNumber++;
        }

        _logger?.Info(
            $"Completed {datasetName} pagination: " +
            $"{pageNumber} page(s), " +
            $"{allItems.Count} item(s)."
        );

        return allItems;
    }

    private async Task<TPage> GetPageAsync<TPage>(
        string requestPath,
        CancellationToken cancellationToken,
        string? accessToken = null)
        where TPage : class
    {
        using var response =
            accessToken is null
                ? await _apiClient.GetAsync(
                    requestPath,
                    cancellationToken
                )
                : await _apiClient.GetWithAccessTokenAsync(
                    requestPath,
                    accessToken,
                    cancellationToken
                );

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<TPage>(
                cancellationToken:
                    cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty API response."
            );
    }

    private static string? GetAlertAfterCursor(
        AlertPageDto page)
    {
        if (!page.Paging.Cursors.TryGetValue(
                "after",
                out var after))
        {
            return null;
        }

        return after.ValueKind
            == JsonValueKind.String
                ? after.GetString()
                : null;
    }
}
