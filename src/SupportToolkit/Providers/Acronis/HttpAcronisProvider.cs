using System.Net.Http.Json;
using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Production Acronis provider backed by the Acronis HTTP APIs.
///
/// Transport concerns such as endpoint paths, pagination, cursor handling,
/// and response deserialization are contained here so consuming modules
/// only depend on IAcronisProvider.
/// </summary>
public sealed class HttpAcronisProvider : IAcronisProvider
{
    /*
     * Conservative page size until production API behaviour is validated.
     */
    private const int PageSize = 100;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public HttpAcronisProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient = apiClient;
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

        return tenants;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Do not filter to resource.machine here.
         *
         * BackupHealth may eventually need non-machine workloads such as
         * Microsoft 365 resources. Filtering belongs here only once actual
         * production resource types have been validated.
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

        return resources;
    }

    public async Task<IReadOnlyList<AlertDto>>
        GetAlertsAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Only active alerts are relevant to the current exception report.
         *
         * We intentionally avoid filtering by alert category/type until
         * production data confirms which Acronis alerts are relevant.
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
    /// Fetches every page for a cursor-paginated Acronis endpoint and
    /// exposes the caller to one combined collection.
    /// </summary>
    private async Task<IReadOnlyList<TItem>>
        FetchAllPagesAsync<TPage, TItem>(
            string datasetName,
            string firstRequestPath,
            string endpointPath,
            Func<TPage, IEnumerable<TItem>> getItems,
            Func<TPage, string?> getAfterCursor,
            CancellationToken cancellationToken)
        where TPage : class
    {
        var allItems =
            new List<TItem>();

        string? requestPath =
            firstRequestPath;

        var pageNumber = 1;

        while (requestPath is not null)
        {
            _logger?.Info(
                $"Fetching {datasetName} page " +
                $"{pageNumber}."
            );

            var page =
                await GetPageAsync<TPage>(
                    requestPath,
                    cancellationToken
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

            if (string.IsNullOrWhiteSpace(after))
            {
                requestPath = null;
                continue;
            }

            /*
             * Acronis cursors are opaque.
             *
             * Do not parse or reconstruct them. URL-encode the cursor and
             * return it exactly as supplied.
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

    /// <summary>
    /// Executes one authenticated API request and deserializes its page.
    /// </summary>
    private async Task<TPage> GetPageAsync<TPage>(
        string requestPath,
        CancellationToken cancellationToken)
        where TPage : class
    {
        using var response =
            await _apiClient.GetAsync(
                requestPath,
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

    /*
     * AlertPagingDto currently keeps cursor values as JsonElement because
     * the alert response contract is intentionally permissive.
     */
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