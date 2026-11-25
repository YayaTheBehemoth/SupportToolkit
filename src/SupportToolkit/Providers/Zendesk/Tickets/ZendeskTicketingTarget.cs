using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Tickets;

/// <summary>
/// The active profile's Zendesk ticket destination: an instance and a
/// group. Resolved and validated locally; nothing is called until a
/// session's provider creates a ticket.
/// </summary>
public sealed class ZendeskTicketingTarget : ITicketingTarget
{
    private readonly ResolvedZendeskConnection _connection;
    private readonly ZendeskOptions _options;

    private ZendeskTicketingTarget(
        ResolvedZendeskConnection connection,
        ZendeskOptions options)
    {
        _connection = connection;
        _options = options;

        Destination =
            new TicketDestinationPreview(
                "Zendesk",
                options.BaseUri.Host,
                connection.DestinationName,
                "Group ID",
                connection.GroupId.ToString(CultureInfo.InvariantCulture));
    }

    public static async Task<ZendeskTicketingTarget> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var connection =
            await resolver.GetZendeskConnectionAsync(cancellationToken);

        return new ZendeskTicketingTarget(
            connection,
            ZendeskOptions.Create(
                connection.Subdomain,
                connection.ClientId,
                connection.ClientSecret));
    }

    public TicketDestinationPreview Destination { get; }

    public IReadOnlyList<StatusDetail> Details =>
    [
        new StatusDetail(
            "Instance",
            _options.BaseUri.GetLeftPart(UriPartial.Authority)),
        new StatusDetail("Destination", _connection.DestinationName),
        new StatusDetail("Group ID", Destination.RoutingId),
        new StatusDetail(
            "Submitter user ID",
            _connection.SubmitterId.ToString(CultureInfo.InvariantCulture)),
        new StatusDetail(
            "Requester user ID",
            _connection.RequesterId.ToString(CultureInfo.InvariantCulture))
    ];

    /// <summary>
    /// The actual Zendesk instance and group, not the profile or
    /// destination display name.
    /// </summary>
    public string IdempotencyIdentity =>
        string.Join(
            ':',
            "zendesk",
            _connection.Subdomain.ToLowerInvariant(),
            Destination.RoutingId);

    public ITicketingSession OpenSession(OperationalLogger logger) =>
        ZendeskTicketingSession.Create(
            _options,
            _connection.GroupId,
            new ZendeskTicketIdentity(
                _connection.SubmitterId,
                _connection.RequesterId),
            logger);
}
