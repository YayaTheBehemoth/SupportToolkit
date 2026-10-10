using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Modules.BackupHealth.Services;

/// <summary>
/// Owns the production Acronis transport lifetime required by BackupHealth.
///
/// Configuration is resolved by the workflow before the session is created.
/// The session owns all HTTP resources created for the production operation.
/// </summary>
public sealed class BackupHealthProductionSession
    : IDisposable
{
    private readonly HttpClient _httpClient;

    public BackupHealthService HealthService { get; }

    private BackupHealthProductionSession(
        HttpClient httpClient,
        BackupHealthService healthService)
    {
        _httpClient =
            httpClient;

        HealthService =
            healthService;
    }

    public static BackupHealthProductionSession Create(
        AcronisOptions options,
        OperationalLogger logger)
    {
        ArgumentNullException.ThrowIfNull(
            options
        );

        ArgumentNullException.ThrowIfNull(
            logger
        );

        var innerHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect =
                    false
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
                Timeout =
                    TimeSpan.FromSeconds(
                        30
                    )
            };

        /*
         * Transport-level logging is diagnostic output.
         *
         * Normal interactive users receive progress information from the UI,
         * while developers may enable full provider diagnostics through
         * SUPPORTTOOLKIT_DEBUG=true.
         */
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

        var provider =
            new HttpAcronisProvider(
                apiClient,
                diagnosticLogger
            );

        var healthService =
            new BackupHealthService(
                provider
            );

        return new BackupHealthProductionSession(
            httpClient,
            healthService
        );
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}