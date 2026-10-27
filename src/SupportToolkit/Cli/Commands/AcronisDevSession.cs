using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// A read-only Acronis tenant provider for developer commands, built
/// from the active profile's connection. Credentials are never taken
/// from the command line.
///
/// The read-only handler keeps the established boundary: no external
/// write endpoint is available through this session.
/// </summary>
public sealed class AcronisDevSession : IDisposable
{
    private readonly HttpClient _httpClient;

    private AcronisDevSession(
        HttpClient httpClient,
        AcronisTenantProvider provider)
    {
        _httpClient = httpClient;
        Provider = provider;
    }

    public AcronisTenantProvider Provider { get; }

    public static async Task<AcronisDevSession> CreateAsync(
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
            new HttpClient(
                new AcronisReadOnlyHandler(
                    options.DatacenterUrl,
                    new HttpClientHandler
                    {
                        AllowAutoRedirect = false
                    }))
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

        var logger =
            OperationalLogger.FromEnvironment();

        var diagnosticLogger =
            logger.DebugEnabled ? logger : null;

        return new AcronisDevSession(
            httpClient,
            new AcronisTenantProvider(
                new AcronisApiClient(
                    httpClient,
                    options,
                    logger: diagnosticLogger),
                diagnosticLogger));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
