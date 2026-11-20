using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.OsTicket.Configuration;
using SupportToolkit.Providers.OsTicket.Transport;

namespace SupportToolkit.Providers.OsTicket.Tickets;

/// <summary>
/// The active profile's osTicket ticket destination: a helpdesk and a
/// help topic. Resolved and validated locally; nothing is called until a
/// session's provider creates a ticket.
/// </summary>
public sealed class OsTicketTicketingTarget : ITicketingTarget
{
    /// <summary>
    /// What osTicket cannot confirm, shown before every submission.
    /// </summary>
    public const string Limits =
        "osTicket does not confirm the help topic: an unknown topic ID " +
        "files the ticket in the default department. It has no duplicate " +
        "protection either, so a retry creates another ticket.";

    private readonly ResolvedOsTicketConnection _connection;
    private readonly OsTicketOptions _options;
    private readonly OsTicketRequester _requester;

    private OsTicketTicketingTarget(
        ResolvedOsTicketConnection connection,
        OsTicketOptions options,
        OsTicketRequester requester)
    {
        _connection = connection;
        _options = options;
        _requester = requester;

        Destination =
            new TicketDestinationPreview(
                "osTicket",
                options.BaseUri.Host,
                connection.DestinationName,
                "Help topic ID",
                connection.TopicId.ToString(CultureInfo.InvariantCulture))
            {
                Caveat = Limits
            };
    }

    public static async Task<OsTicketTicketingTarget> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var connection =
            await OsTicketConnections.Instance.ResolveAsync(
                resolver,
                cancellationToken);

        OsTicketRequester requester;

        try
        {
            requester =
                new OsTicketRequester(
                    connection.RequesterName,
                    connection.RequesterEmail);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"osTicket connection '{connection.ConnectionName}' " +
                "needs a requesterName and a valid requesterEmail.",
                exception);
        }

        return new OsTicketTicketingTarget(
            connection,
            OsTicketOptions.Create(connection.BaseUrl, connection.ApiKey),
            requester);
    }

    public TicketDestinationPreview Destination { get; }

    public IReadOnlyList<StatusDetail> Details =>
    [
        new StatusDetail(
            "Helpdesk",
            _options.BaseUri.GetLeftPart(UriPartial.Path)),
        new StatusDetail("Destination", _connection.DestinationName),
        new StatusDetail("Help topic ID", Destination.RoutingId),
        new StatusDetail(
            "Requester",
            $"{_requester.Name} <{_requester.Email}>")
    ];

    /// <summary>
    /// The helpdesk and topic. osTicket does not use it, but it keeps
    /// the retry identity of the same draft distinct per destination.
    /// </summary>
    public string IdempotencyIdentity =>
        string.Join(
            ':',
            "osticket",
            _options.BaseUri.GetLeftPart(UriPartial.Path).ToLowerInvariant(),
            Destination.RoutingId);

    public ITicketingSession OpenSession(OperationalLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new OsTicketTicketingSession(
            OsTicketHttpPipeline.Create(),
            _options,
            _connection.TopicId,
            _requester,
            logger);
    }
}

/// <summary>
/// Owns the osTicket HTTP client and the ticket provider built on it.
/// </summary>
public sealed class OsTicketTicketingSession : ITicketingSession
{
    private readonly HttpClient _httpClient;

    public OsTicketTicketingSession(
        HttpClient httpClient,
        OsTicketOptions options,
        int topicId,
        OsTicketRequester requester,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        TicketProvider =
            new OsTicketTicketProvider(
                new OsTicketApiClient(httpClient, options, logger),
                topicId,
                requester);
    }

    public ITicketProvider TicketProvider { get; }

    public void Dispose() => _httpClient.Dispose();
}
