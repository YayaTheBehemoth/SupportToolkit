using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Microsoft365;

/// <summary>
/// Reads the Microsoft 365 backup inventory exposed by Acronis Resource Manager.
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

        var groups =
            await GetGroupsAsync(
                scopedAccessToken,
                cancellationToken
            );

        var leafGroupIds =
            groups
                .Where(
                    group =>
                        group.Leaf == true
                        && Guid.TryParse(group.Id, out _)
                )
                .Select(group => group.Id!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        _logger?.Debug(
            $"Microsoft 365 queryable leaf groups: {leafGroupIds.Count}."
        );

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

            rawResources.AddRange(groupResources);
        }

        var uniqueResources =
            Deduplicate(rawResources);

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
            BuildInitialResourcePath(groupId);

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

            resources.AddRange(page.Items);

            if (resources.Count > MaxResources)
            {
                throw new InvalidOperationException(
                    $"Microsoft 365 resource fetch exceeded {MaxResources} resources."
                );
            }

            _logger?.Debug(
                $"Microsoft 365 resource page {pageNumber}: " +
                $"{page.Items.Count} item(s)."
            );

            var after =
                page.Paging.Cursors.After;

            requestPath =
                string.IsNullOrWhiteSpace(after)
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
                string.IsNullOrWhiteSpace(resource.Id)
                    ? resource.InternalId
                    : resource.Id;

            if (string.IsNullOrWhiteSpace(identity))
            {
                withoutIdentity.Add(resource);
                continue;
            }

            unique[identity] = resource;
        }

        return unique.Values
            .Concat(withoutIdentity)
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
