using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Modules.Ticketing.Services;

/// <summary>
/// Owns the runtime lifetime of the configured external ticketing provider.
///
/// Zendesk is currently the concrete production provider, but callers depend
/// only on the SupportToolkit ticketing abstraction.
/// </summary>
public sealed class TicketingProductionSession
    : IDisposable
{
    private readonly HttpClient _httpClient;

    public ITicketProvider TicketProvider { get; }

    private TicketingProductionSession(
        HttpClient httpClient,
        ITicketProvider ticketProvider)
    {
        _httpClient =
            httpClient;

        TicketProvider =
            ticketProvider;
    }

    public static TicketingProductionSession Create(
        OperationalLogger logger)
    {
        var options =
            ZendeskOptions.FromEnvironment();

        var handler =
            new HttpClientHandler
            {
                /*
                 * Do not allow authenticated requests to follow redirects
                 * automatically.
                 */
                AllowAutoRedirect =
                    false
            };

        var httpClient =
            new HttpClient(
                handler
            )
            {
                Timeout =
                    TimeSpan.FromSeconds(
                        30
                    )
            };

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                options,
                logger:
                    logger
            );

        var ticketProvider =
            new ZendeskTicketProvider(
                apiClient
            );

        return new TicketingProductionSession(
            httpClient,
            ticketProvider
        );
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}