using SupportToolkit.Providers.Acronis.Alerts.Dtos;
using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Providers.Acronis;


/// Defines the boundary between SupportToolkit and Acronis.
/// Consuming modules can access Acronis data through this contract without depending on HTTP,
/// authentication, pagination, fixture files, or other transport details.
/// Both fixture and production HTTP implementations satisfy this contract.

public interface IAcronisProvider
{


    Task<IReadOnlyList<ResourceStatusDto>> GetResourceStatusesAsync(
        CancellationToken cancellationToken = default
    );





    Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
        CancellationToken cancellationToken = default
    );



    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        CancellationToken cancellationToken = default
    );
}
