using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Owns the production Acronis transport lifetime and composes the narrowly
/// scoped providers required by BackupAggregator.
/// </summary>
public sealed class BackupAggregatorProductionSession
    : IDisposable
{
    private readonly HttpClient _httpClient;

    public BackupInventoryReviewService ReviewService { get; }

    private BackupAggregatorProductionSession(
        HttpClient httpClient,
        BackupInventoryReviewService reviewService)
    {
        _httpClient = httpClient;
        ReviewService = reviewService;
    }

    public static BackupAggregatorProductionSession Create(
        OperationalLogger logger)
    {
        var options =
            AcronisOptions.FromEnvironment();

        var innerHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect = false
            };

        var readOnlyHandler =
            new AcronisReadOnlyHandler(
                options.DatacenterUrl,
                innerHandler
            );

        var httpClient =
            new HttpClient(
                readOnlyHandler
            )
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

        var diagnosticLogger =
            logger.DebugEnabled
                ? logger
                : null;

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options,
                logger:
                    diagnosticLogger
            );

        var tenantProvider =
            new AcronisTenantProvider(
                apiClient,
                diagnosticLogger
            );

        var microsoft365Inventory =
            new AcronisMicrosoft365InventoryProvider(
                apiClient,
                diagnosticLogger
            );

        var deviceInventory =
            new AcronisDeviceInventoryProvider(
                apiClient,
                diagnosticLogger
            );

        var reviewService =
            new BackupInventoryReviewService(
                new AcronisTenantResolver(
                    tenantProvider
                ),
                microsoft365Inventory,
                deviceInventory,
                new BackupInventoryNormalizer(),
                logger
            );

        return new BackupAggregatorProductionSession(
            httpClient,
            reviewService
        );
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
