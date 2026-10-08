using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Alerts.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Alerts;

public sealed class AcronisAlertProvider
    : IAcronisAlertProvider
{
    private const int PageSize = 100;

    private readonly AcronisCursorPaginator _paginator;
    private readonly OperationalLogger? _logger;

    public AcronisAlertProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _paginator =
            new AcronisCursorPaginator(
                apiClient,
                logger
            );

        _logger = logger;
    }

    public async Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Fetching active Acronis alerts."
        );

        var alerts =
            await _paginator.FetchAllAsync<
                AlertPageDto,
                AlertDto>(
                datasetName:
                    "alerts",
                firstRequestPath:
                    "/api/alert_manager/v1/alerts" +
                    "?show_deleted=false" +
                    $"&limit={PageSize}",
                buildNextRequestPath:
                    after =>
                        "/api/alert_manager/v1/alerts" +
                        $"?limit={PageSize}" +
                        $"&after={Uri.EscapeDataString(after)}",
                getItems:
                    page => page.Items,
                getAfterCursor:
                    GetAfterCursor,
                cancellationToken:
                    cancellationToken
            );

        _logger?.Info(
            $"Active Acronis alerts fetched: {alerts.Count}."
        );

        return alerts;
    }

    private static string? GetAfterCursor(
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
