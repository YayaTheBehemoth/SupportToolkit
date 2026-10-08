using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.ResourceManagement;

public sealed class AcronisResourceStatusProvider
    : IAcronisResourceStatusProvider
{
    private const int PageSize = 100;

    private readonly AcronisApiClient _apiClient;
    private readonly AcronisCursorPaginator _paginator;
    private readonly OperationalLogger? _logger;

    public AcronisResourceStatusProvider(
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

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Fetching Acronis resource statuses."
        );

        var resources =
            await FetchAsync(
                datasetName:
                    "resource statuses",
                cancellationToken:
                    cancellationToken
            );

        if (resources.Count == 0)
        {
            _logger?.Warning(
                "Acronis returned zero resource statuses. " +
                "Verify API-client scope and resource-status visibility."
            );
        }

        _logger?.Info(
            $"Acronis resource statuses fetched: {resources.Count}."
        );

        return resources;
    }

    public async Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(tenantId, out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for a customer-scoped " +
                "resource-status request.",
                nameof(tenantId)
            );
        }

        var scopedAccessToken =
            await _apiClient.GetScopedAccessTokenAsync(
                tenantId,
                cancellationToken
            );

        return await FetchAsync(
            datasetName:
                "customer-scoped resource statuses",
            cancellationToken:
                cancellationToken,
            accessToken:
                scopedAccessToken
        );
    }

    private Task<IReadOnlyList<ResourceStatusDto>> FetchAsync(
        string datasetName,
        CancellationToken cancellationToken,
        string? accessToken = null)
    {
        return _paginator.FetchAllAsync<
            ResourceStatusPageDto,
            ResourceStatusDto>(
                datasetName:
                    datasetName,
                firstRequestPath:
                    "/api/resource_management/v4/resource_statuses" +
                    $"?limit={PageSize}",
                buildNextRequestPath:
                    after =>
                        "/api/resource_management/v4/resource_statuses" +
                        $"?limit={PageSize}" +
                        $"&after={Uri.EscapeDataString(after)}",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    page => page.Paging.Cursors.After,
                cancellationToken:
                    cancellationToken,
                accessToken:
                    accessToken
            );
    }
}
