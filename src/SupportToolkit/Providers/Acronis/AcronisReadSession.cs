using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Read-only Acronis tenant and usage providers, built from the active
/// profile's connection. Credentials are never taken from a command line.
///
/// Shared by modules and developer commands. The read-only handler keeps
/// the established boundary: no external write endpoint is available
/// through this session.
/// </summary>
public sealed class AcronisReadSession : IDisposable
{
    private readonly HttpClient _httpClient;

    private AcronisReadSession(
        HttpClient httpClient,
        AcronisTenantProvider provider,
        AcronisUsageProvider usageProvider)
    {
        _httpClient = httpClient;
        Provider = provider;
        UsageProvider = usageProvider;
    }

    public AcronisTenantProvider Provider { get; }

    public AcronisUsageProvider UsageProvider { get; }

    public static async Task<AcronisReadSession> CreateAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        var connection =
            await configurationResolver.GetAcronisConnectionAsync(
                cancellationToken);

        var options =
            AcronisOptions.Create(
                connection.DatacenterUrl,
                connection.ClientId,
                connection.ClientSecret);

        var httpClient =
            AcronisHttpPipeline.Create(options);

        var logger =
            OperationalLogger.FromEnvironment();

        var diagnosticLogger =
            logger.DebugEnabled ? logger : null;

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options,
                logger: diagnosticLogger);

        return new AcronisReadSession(
            httpClient,
            new AcronisTenantProvider(
                apiClient,
                diagnosticLogger),
            new AcronisUsageProvider(
                apiClient,
                diagnosticLogger));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
