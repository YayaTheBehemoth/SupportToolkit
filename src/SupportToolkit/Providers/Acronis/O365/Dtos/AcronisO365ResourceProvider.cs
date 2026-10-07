using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.O365.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.O365;

public sealed class AcronisO365ResourceProvider
{
    private const int PageSize =
        100;

    private const int MaxPages =
        100;

    private const int MaxResources =
        10_000;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisO365ResourceProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient;

        _logger =
            logger;
    }

    public async Task<IReadOnlyList<AcronisO365ResourceDto>>
        GetResourcesForGroupAsync(
            string tenantId,
            string groupId,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(
                tenantId,
                out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for customer-scoped authentication.",
                nameof(tenantId)
            );
        }

        if (!Guid.TryParse(
                groupId,
                out _))
        {
            throw new ArgumentException(
                "A valid O365 group UUID is required.",
                nameof(groupId)
            );
        }

        var scopedAccessToken =
            await _apiClient
                .GetScopedAccessTokenAsync(
                    tenantId,
                    cancellationToken
                );

        var resources =
            new List<AcronisO365ResourceDto>();

        string? requestPath =
            BuildInitialPath(
                groupId
            );

        var pageNumber =
            1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"O365 resource pagination exceeded {MaxPages} pages."
                );
            }

            _logger?.Info(
                $"Fetching O365 resource page {pageNumber}."
            );

            using var response =
                await _apiClient
                    .GetWithAccessTokenAsync(
                        requestPath,
                        scopedAccessToken,
                        cancellationToken
                    );

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<AcronisO365ResourcePageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty O365 resource response."
                );

            resources.AddRange(
                page.Items
            );

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"O365 resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Info(
                $"O365 resource page {pageNumber}: " +
                $"{page.Items.Count} item(s)."
            );

            var after =
                page.Paging.Cursors.After;

            if (string.IsNullOrWhiteSpace(
                    after))
            {
                requestPath =
                    null;
            }
            else
            {
                requestPath =
                    BuildNextPath(
                        groupId,
                        after
                    );

                pageNumber++;
            }
        }

        _logger?.Info(
            $"O365 resources fetched: {resources.Count}."
        );

        return resources
            .AsReadOnly();
    }

    private static string BuildInitialPath(
        string groupId)
    {
        return
            "/api/resource_manager/v1/o365/" +
            $"groups/{Uri.EscapeDataString(groupId)}/resources" +
            $"?limit={PageSize}" +
            "&order=asc(last_task_status)";
    }

    private static string BuildNextPath(
        string groupId,
        string after)
    {
        return
            "/api/resource_manager/v1/o365/" +
            $"groups/{Uri.EscapeDataString(groupId)}/resources" +
            $"?limit={PageSize}" +
            "&order=asc(last_task_status)" +
            $"&after={Uri.EscapeDataString(after)}";
    }
}