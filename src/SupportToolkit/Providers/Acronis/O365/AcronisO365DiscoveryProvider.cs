using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.O365.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.O365;

/// <summary>
/// Discovers the Microsoft 365 application/account and group hierarchy
/// visible to one customer-scoped Acronis tenant.
///
/// This provider intentionally performs discovery only. Resource retrieval
/// remains the responsibility of AcronisO365ResourceProvider.
/// </summary>
public sealed class AcronisO365DiscoveryProvider
{
    private const int MaxGroupRequests =
        500;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisO365DiscoveryProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient;

        _logger =
            logger;
    }

    public async Task<AcronisO365DiscoveryResult>
        DiscoverAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(
                tenantId,
                out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for O365 discovery.",
                nameof(tenantId)
            );
        }

        var scopedAccessToken =
            await _apiClient
                .GetScopedAccessTokenAsync(
                    tenantId,
                    cancellationToken
                );

        var applications =
            await GetApplicationsAsync(
                scopedAccessToken,
                cancellationToken
            );

        /*
         * A customer-scoped token should already constrain the response.
         *
         * We still prefer applications whose tenantId matches the tenant
         * supplied by Account Management when that information is present.
         */
        var o365Applications =
            applications
                .Where(
                    application =>
                        string.Equals(
                            application.Suite,
                            "o365",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        var exactTenantMatches =
            o365Applications
                .Where(
                    application =>
                        string.Equals(
                            application.TenantId,
                            tenantId,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        var selectedApplications =
            exactTenantMatches.Count > 0
                ? exactTenantMatches
                : o365Applications;

        var accountIds =
            selectedApplications
                .Select(
                    application =>
                        application.AccountId
                )
                .Where(
                    accountId =>
                        !string.IsNullOrWhiteSpace(
                            accountId
                        )
                )
                .Select(
                    accountId =>
                        accountId!
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

        _logger?.Info(
            $"O365 applications discovered: " +
            $"{selectedApplications.Count}."
        );

        _logger?.Info(
            $"O365 account roots discovered: " +
            $"{accountIds.Count}."
        );

        var groups =
            await DiscoverGroupsAsync(
                accountIds,
                scopedAccessToken,
                cancellationToken
            );

        return new AcronisO365DiscoveryResult(
            selectedApplications.AsReadOnly(),
            groups
        );
    }

    private async Task<IReadOnlyList<AcronisO365ApplicationDto>>
        GetApplicationsAsync(
            string accessToken,
            CancellationToken cancellationToken)
    {
        const string requestPath =
            "/api/resource_manager/v1/o365/applications";

        _logger?.Info(
            "Fetching O365 applications."
        );

        using var response =
            await _apiClient
                .GetWithAccessTokenAsync(
                    requestPath,
                    accessToken,
                    cancellationToken
                );

        response.EnsureSuccessStatusCode();

        var payload =
            await response.Content
                .ReadFromJsonAsync<AcronisO365ApplicationsDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty O365 applications response."
            );

        return payload.Applications;
    }

    private async Task<IReadOnlyList<AcronisO365GroupDto>>
        DiscoverGroupsAsync(
            IReadOnlyList<string> accountIds,
            string accessToken,
            CancellationToken cancellationToken)
    {
        var discovered =
            new Dictionary<
                string,
                AcronisO365GroupDto>(
                    StringComparer.OrdinalIgnoreCase
                );

        var visitedParents =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        var queue =
            new Queue<string>(
                accountIds
            );

        var requestCount =
            0;

        while (queue.Count > 0)
        {
            var parentId =
                queue.Dequeue();

            if (!visitedParents.Add(
                    parentId))
            {
                continue;
            }

            requestCount++;

            if (requestCount
                > MaxGroupRequests)
            {
                throw new InvalidOperationException(
                    $"O365 group discovery exceeded " +
                    $"{MaxGroupRequests} requests."
                );
            }

            var groups =
                await GetChildGroupsAsync(
                    parentId,
                    accessToken,
                    cancellationToken
                );

            foreach (var group
                     in groups)
            {
                if (string.IsNullOrWhiteSpace(
                        group.Id))
                {
                    _logger?.Warning(
                        "Acronis returned an O365 group without an ID."
                    );

                    continue;
                }

                discovered[group.Id] =
                    group;

                /*
                 * Production payloads expose a leaf flag.
                 * Only non-leaf or unknown nodes are considered potential
                 * parents for another traversal step.
                 */
                if (group.Leaf != true)
                {
                    queue.Enqueue(
                        group.Id
                    );
                }
            }
        }

        _logger?.Info(
            $"O365 groups discovered: " +
            $"{discovered.Count}."
        );

        return discovered
            .Values
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<AcronisO365GroupDto>>
        GetChildGroupsAsync(
            string parentId,
            string accessToken,
            CancellationToken cancellationToken)
    {
        var requestPath =
            "/api/resource_manager/v1/o365/groups" +
            $"?parentId={Uri.EscapeDataString(parentId)}";

        _logger?.Debug(
            "Fetching O365 child groups."
        );

        using var response =
            await _apiClient
                .GetWithAccessTokenAsync(
                    requestPath,
                    accessToken,
                    cancellationToken
                );

        response.EnsureSuccessStatusCode();

        var payload =
            await response.Content
                .ReadFromJsonAsync<AcronisO365GroupsDto>(
                    cancellationToken:
                        cancellationToken
                )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty O365 group response."
            );

        return payload.Groups;
    }
}

public sealed record AcronisO365DiscoveryResult(
    IReadOnlyList<AcronisO365ApplicationDto> Applications,
    IReadOnlyList<AcronisO365GroupDto> Groups
);