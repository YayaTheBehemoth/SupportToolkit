using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Alerts;
using SupportToolkit.Providers.Acronis.Alerts.Dtos;
using SupportToolkit.Providers.Acronis.ResourceManagement;
using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Compatibility facade for the original broad Acronis provider contract.
///
/// Endpoint-specific behavior now lives in capability-focused providers.
/// New code should depend on those narrower interfaces directly.
/// </summary>
public sealed class HttpAcronisProvider
    : IAcronisProvider,
      IAcronisTenantMappingProvider
{
    private readonly AcronisTenantProvider _tenants;
    private readonly AcronisResourceStatusProvider _resourceStatuses;
    private readonly AcronisAlertProvider _alerts;

    public HttpAcronisProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _tenants =
            new AcronisTenantProvider(
                apiClient,
                logger
            );

        _resourceStatuses =
            new AcronisResourceStatusProvider(
                apiClient,
                logger
            );

        _alerts =
            new AcronisAlertProvider(
                apiClient,
                logger
            );
    }

    public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return _tenants.GetTenantsAsync(
            cancellationToken
        );
    }

    public Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesAsync(
            CancellationToken cancellationToken = default)
    {
        return _resourceStatuses.GetResourceStatusesAsync(
            cancellationToken
        );
    }

    public Task<IReadOnlyList<ResourceStatusDto>>
        GetResourceStatusesForTenantAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
    {
        return _resourceStatuses.GetResourceStatusesForTenantAsync(
            tenantId,
            cancellationToken
        );
    }

    public Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        CancellationToken cancellationToken = default)
    {
        return _alerts.GetAlertsAsync(
            cancellationToken
        );
    }

    public Task<IReadOnlyDictionary<string, string>>
        GetTenantIdMappingsAsync(
            CancellationToken cancellationToken = default)
    {
        return _tenants.GetTenantIdMappingsAsync(
            cancellationToken
        );
    }
}
