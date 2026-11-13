
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Tickets;

/// <summary>
/// Owns the Zendesk ticketing transport and the ticket provider built on it.
///
/// Callers depend only on the SupportToolkit ticketing capability
/// (ITicketProvider).
///
/// Configuration and destination routing are resolved by the caller before
/// the session is created.
/// </summary>
public sealed class ZendeskTicketingSession
    : IDisposable
{
    private readonly HttpClient _httpClient;

    public ITicketProvider TicketProvider { get; }

    private ZendeskTicketingSession(
        HttpClient httpClient,
        ITicketProvider ticketProvider)
    {
        _httpClient =
            httpClient;

        TicketProvider =
            ticketProvider;
    }

    public static ZendeskTicketingSession Create(
        ZendeskOptions options,
        long groupId,
        ZendeskTicketIdentity identity,
        OperationalLogger logger)
    {
        ArgumentNullException.ThrowIfNull(
            options
        );

        ArgumentNullException.ThrowIfNull(
            identity
        );

        ArgumentNullException.ThrowIfNull(
            logger
        );

        if (groupId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(groupId),
                groupId,
                "A positive Zendesk destination group ID is required."
            );
        }

        var httpClient =
            ZendeskTicketingHttpPipeline.Create();

        /*
         * The ticketing connection authenticates with its own
         * client credentials source and the tickets:write scope.
         */
        var tokenSource =
            new ZendeskClientCredentialsTokenSource(
                httpClient,
                options,
                logger:
                    logger
            );

        var apiClient =
            new ZendeskApiClient(
                httpClient,
                options,
                tokenSource,
                logger
            );

        var ticketProvider =
            new ZendeskTicketProvider(
                apiClient,
                groupId,
                identity
            );

        return new ZendeskTicketingSession(
            httpClient,
            ticketProvider
        );
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
