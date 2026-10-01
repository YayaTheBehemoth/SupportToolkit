using System.Net.Http.Json;
using System.Text.Json;
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
    // Conservative page size until production API behaviour is validated.
    private const int PageSize = 100;

    private readonly AcronisApiClient _apiClient;

    public HttpAcronisProvider(
        AcronisApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        var rootTenantId =
            await _apiClient.GetRootTenantIdAsync(
                cancellationToken
            );

        var firstRequestPath =
            "/api/2/tenants" +
            $"?subtree_root_id={Uri.EscapeDataString(rootTenantId)}" +
            "&lod=basic" +
            $"&limit={PageSize}";

        return await FetchAllPagesAsync<
            TenantPageDto,
            TenantDto>(
            firstRequestPath,
            "/api/2/tenants",
            page => page.Items,
            page => page.Paging.Cursors.After,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        /*
         * Do not filter to resource.machine here.
         *
         * BackupHealth may eventually need non-machine workloads such as
         * Microsoft 365 resources. Filtering belongs here only once the
         * actual production resource types have been validated.
         */
        var firstRequestPath =
            "/api/resource_management/v4/resource_statuses" +
            $"?limit={PageSize}";

        return await FetchAllPagesAsync<
            ResourceStatusPageDto,
            ResourceStatusDto>(
            firstRequestPath,
            "/api/resource_management/v4/resource_statuses",
            page => page.Items,
            page => page.Paging.Cursors.After,
            cancellationToken
        );
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * Only active alerts are relevant to the current exception report.
         * We intentionally avoid filtering by alert category/type until
         * production data confirms which Acronis alerts are relevant.
         */
        var firstRequestPath =
            "/api/alert_manager/v1/alerts" +
            $"?show_deleted=false&limit={PageSize}";

        return await FetchAllPagesAsync<
            AlertPageDto,
            AlertDto>(
            firstRequestPath,
            "/api/alert_manager/v1/alerts",
            page => page.Items,
            GetAlertAfterCursor,
            cancellationToken
        );
    }

    /// <summary>
    /// Fetches every page for a cursor-paginated Acronis endpoint and
    /// exposes the caller to one combined collection.
    /// </summary>
    private async Task<IReadOnlyList<TItem>>
        FetchAllPagesAsync<TPage, TItem>(
            string firstRequestPath,
            string endpointPath,
            Func<TPage, IEnumerable<TItem>> getItems,
            Func<TPage, string?> getAfterCursor,
            CancellationToken cancellationToken)
        where TPage : class
    {
        var allItems = new List<TItem>();

        string? requestPath =
            firstRequestPath;

        while (requestPath is not null)
        {
            var page = await GetPageAsync<TPage>(
                requestPath,
                cancellationToken
            );

            allItems.AddRange(
                getItems(page)
            );

            var after =
                getAfterCursor(page);

            if (string.IsNullOrWhiteSpace(after))
            {
                requestPath = null;
                continue;
            }

            /*
             * Acronis cursors are opaque. Do not parse or reconstruct them.
             * URL-encode the cursor and return it exactly as supplied.
             *
             * The cursor carries the original query state, so subsequent
             * requests only need the endpoint, limit, and cursor.
             */
            requestPath =
                endpointPath +
                $"?limit={PageSize}" +
                $"&after={Uri.EscapeDataString(after)}";
        }

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
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                $"Acronis returned an empty response for '{requestPath}'."
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

        return after.ValueKind == JsonValueKind.String
            ? after.GetString()
            : null;
    }
}