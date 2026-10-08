using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.O365.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.O365;

/// <summary>
/// Discovers the Microsoft 365 group hierarchy exposed by Acronis
/// Resource Manager for one customer-scoped tenant.
///
/// This uses the same read-only endpoint observed in the Acronis web client.
/// Resource retrieval remains the responsibility of
/// AcronisO365ResourceProvider.
/// </summary>
public sealed class AcronisO365DiscoveryProvider
{
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

        const string requestPath =
            "/bc/api/resource_manager/v1/o365/groups";

        _logger?.Info(
            "Fetching customer-scoped O365 groups."
        );

        using var response =
            await _apiClient
                .GetWithAccessTokenAsync(
                    requestPath,
                    scopedAccessToken,
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
                "Acronis returned an empty O365 groups response."
            );

        var groups =
            payload.Groups
                .Where(
                    group =>
                        !string.IsNullOrWhiteSpace(
                            group.Id
                        )
                )
                .ToList()
                .AsReadOnly();

        _logger?.Info(
            $"O365 groups discovered: {groups.Count}."
        );

        return new AcronisO365DiscoveryResult(
            groups
        );
    }
}

public sealed record AcronisO365DiscoveryResult(
    IReadOnlyList<AcronisO365GroupDto> Groups
);