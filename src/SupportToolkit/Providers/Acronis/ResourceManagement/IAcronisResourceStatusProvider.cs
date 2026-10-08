using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;

namespace SupportToolkit.Providers.Acronis.ResourceManagement;

public interface IAcronisResourceStatusProvider
{
    Task<IReadOnlyList<ResourceStatusDto>> GetResourceStatusesAsync(
        CancellationToken cancellationToken = default
    );
}
