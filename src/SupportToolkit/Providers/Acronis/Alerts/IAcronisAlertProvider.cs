using SupportToolkit.Providers.Acronis.Alerts.Dtos;

namespace SupportToolkit.Providers.Acronis.Alerts;

public interface IAcronisAlertProvider
{
    Task<IReadOnlyList<AlertDto>> GetAlertsAsync(
        CancellationToken cancellationToken = default
    );
}
