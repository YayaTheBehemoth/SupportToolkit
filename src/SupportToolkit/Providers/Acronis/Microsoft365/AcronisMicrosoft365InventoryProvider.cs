using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Microsoft365;

/// <summary>
/// Reads the Microsoft 365 backup inventory exposed by Acronis Resource Manager.
///
/// A tenant that does not have Microsoft 365 configured is represented as a
/// valid empty Microsoft 365 inventory rather than as a failed tenant review.
///
/// The provider discovers queryable leaf groups, follows resource pagination,
/// and removes duplicate resource identities caused by overlapping groups.
/// </summary>
public sealed class AcronisMicrosoft365InventoryProvider
    : IAcronisMicrosoft365InventoryProvider
{
    private const int PageSize = 30;
    private const int MaxPages = 500;
    private const int MaxResources = 25_000;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisMicrosoft365InventoryProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
        GetResourcesForTenantAsync(
            string tenantId,
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

        var scopedAccessToken =
            await _apiClient.GetScopedAccessTokenAsync(
                tenantId,
                cancellationToken
            );

        var groups =
            await GetGroupsAsync(
                scopedAccessToken,
                cancellationToken
            );

        /*
         * A successful empty group collection is a valid state.
         *
         * In production this occurs for customer tenants where the Microsoft
         * 365 workload is not configured at all. Their device inventory must
         * still be reviewed normally.
         */
        if (groups.Count == 0)
        {
            _logger?.Debug(
                "Microsoft 365 inventory is not configured or contains no groups."
            );

            return [];
        }

        var leafGroupIds =
            groups
                .Where(
                    group =>
                        group.Leaf == true
                        && Guid.TryParse(
                            group.Id,
                            out _
                        )
                )
                .Select(
                    group =>
                        group.Id!
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        _logger?.Debug(
            $"Microsoft 365 queryable leaf groups: {leafGroupIds.Count}."
        );

        if (leafGroupIds.Count == 0)
        {
            /*
             * The endpoint responded successfully but exposed no queryable
             * Microsoft 365 resource groups. This is still a valid empty
             * inventory rather than a transport failure.
             */
            return [];
        }

        var rawResources =
            new List<AcronisMicrosoft365ResourceDto>();

        foreach (var groupId in leafGroupIds)
        {
            var groupResources =
                await GetResourcesForGroupAsync(
                    groupId,
                    scopedAccessToken,
                    cancellationToken
                );

            rawResources.AddRange(
                groupResources
            );
        }

        var uniqueResources =
            Deduplicate(
                rawResources
            );

        _logger?.Debug(
            $"Microsoft 365 inventory rows: {rawResources.Count} raw, " +
            $"{uniqueResources.Count} unique."
        );

        return uniqueResources;
    }

    private async Task<IReadOnlyList<AcronisMicrosoft365GroupDto>>
        GetGroupsAsync(
            string scopedAccessToken,
            CancellationToken cancellationToken)
    {
        const string requestPath =
            "/bc/api/resource_manager/v1/o365/groups";

        using var response =
            await _apiClient.GetWithAccessTokenAsync(
                requestPath,
                scopedAccessToken,
                cancellationToken
            );

        /*
         * HTTP failures remain failures.
         *
         * Only a successful response containing no Microsoft 365 groups is
         * normalized to an empty inventory.
         */
        response.EnsureSuccessStatusCode();

        var payload =
            await response.Content
                .ReadFromJsonAsync<AcronisMicrosoft365GroupsDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty Microsoft 365 groups response."
            );

        return payload.Groups;
    }

    private async Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
        GetResourcesForGroupAsync(
            string groupId,
            string scopedAccessToken,
            CancellationToken cancellationToken)
    {
        var resources =
            new List<AcronisMicrosoft365ResourceDto>();

        string? requestPath =
            BuildInitialResourcePath(
                groupId
            );

        var pageNumber = 1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Microsoft 365 resource pagination exceeded {MaxPages} pages."
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
                    .ReadFromJsonAsync<AcronisMicrosoft365ResourcePageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty Microsoft 365 resource response."
                );

            /*
             * Explicit JSON null for an items collection represents an empty
             * page. Null elements themselves are ignored because there is no
             * resource object to classify.
             */
            var pageItems =
                page.Items?
                    .Where(
                        item =>
                            item is not null
                    )
                    .Select(
                        item =>
                            item!
                    )
                    .ToList()
                ?? [];

            resources.AddRange(
                pageItems
            );

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"Microsoft 365 resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Debug(
                $"Microsoft 365 resource page {pageNumber}: " +
                $"{pageItems.Count} item(s)."
            );

            var after =
                page.Paging?
                    .Cursors?
                    .After;

            requestPath =
                string.IsNullOrWhiteSpace(
                    after
                )
                    ? null
                    : BuildNextResourcePath(
                        groupId,
                        after
                    );

            pageNumber++;
        }

        return resources.AsReadOnly();
    }

    private static IReadOnlyList<AcronisMicrosoft365ResourceDto>
        Deduplicate(
            IEnumerable<AcronisMicrosoft365ResourceDto> resources)
    {
        var unique =
            new Dictionary<string, AcronisMicrosoft365ResourceDto>(
                StringComparer.OrdinalIgnoreCase
            );

        var withoutIdentity =
            new List<AcronisMicrosoft365ResourceDto>();

        foreach (var resource in resources)
        {
            var identity =
                string.IsNullOrWhiteSpace(
                    resource.Id
                )
                    ? resource.InternalId
                    : resource.Id;

            if (string.IsNullOrWhiteSpace(
                    identity
                ))
            {
                withoutIdentity.Add(
                    resource
                );

                continue;
            }

            unique[identity] =
                resource;
        }

        return unique.Values
            .Concat(
                withoutIdentity
            )
            .ToList()
            .AsReadOnly();
    }

    private static string BuildInitialResourcePath(
        string groupId)
    {
        return
            "/bc/api/resource_manager/v1/o365/" +
            $"groups/{Uri.EscapeDataString(groupId)}/resources" +
            $"?limit={PageSize}" +
            "&order=asc(last_task_status)";
    }

    private static string BuildNextResourcePath(
        string groupId,
        string after)
    {
        return
            "/bc/api/resource_manager/v1/o365/" +
            $"groups/{Uri.EscapeDataString(groupId)}/resources" +
            $"?after={Uri.EscapeDataString(after)}";
    }
}