using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Providers.Zammad.Configuration;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Providers.Zammad.Tickets;

/// <summary>
/// The active profile's Zammad ticket destination: an instance and a
/// group. Resolved and validated locally; nothing is called until a
/// session's provider creates a ticket.
/// </summary>
public sealed class ZammadTicketingTarget : ITicketingTarget
{
    /// <summary>
    /// What Zammad cannot protect against, shown before every submission.
    /// The group itself is confirmed: Zammad refuses an unknown one.
    /// </summary>
    public const string Limits =
        "Zammad has no duplicate protection, so a retry creates another " +
        "ticket.";

    private readonly ResolvedZammadConnection _connection;
    private readonly ZammadOptions _options;

    private ZammadTicketingTarget(
        ResolvedZammadConnection connection,
        ZammadOptions options)
    {
        _connection = connection;
        _options = options;

        Destination =
            new TicketDestinationPreview(
                "Zammad",
                options.BaseUri.Host,
                connection.DestinationName,
                "Group",
                connection.Group)
            {
                Caveat = Limits
            };
    }

    public static async Task<ZammadTicketingTarget> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var connection =
            await ZammadConnections.Instance.ResolveAsync(
                resolver,
                cancellationToken);

        var customer = connection.Customer;

        if (customer.IndexOf('@') <= 0 || customer.EndsWith('@'))
        {
            throw new InvalidOperationException(
                $"Zammad connection '{connection.ConnectionName}' needs a " +
                "valid customer email address.");
        }

        return new ZammadTicketingTarget(
            connection,
            ZammadOptions.Create(connection.BaseUrl, connection.ApiToken));
    }

    public TicketDestinationPreview Destination { get; }

    public IReadOnlyList<StatusDetail> Details =>
    [
        new StatusDetail(
            "Instance",
            _options.BaseUri.GetLeftPart(UriPartial.Path)),
        new StatusDetail("Destination", _connection.DestinationName),
        new StatusDetail("Group", _connection.Group),
        new StatusDetail("Customer", _connection.Customer)
    ];

    /// <summary>
    /// The instance and group. Zammad does not use it, but it keeps the
    /// retry identity of the same draft distinct per destination.
    /// </summary>
    public string IdempotencyIdentity =>
        string.Join(
            ':',
            "zammad",
            _options.BaseUri.GetLeftPart(UriPartial.Path).ToLowerInvariant(),
            _connection.Group.ToLowerInvariant());

    public ITicketingSession OpenSession(OperationalLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new ZammadTicketingSession(
            ZammadHttpPipeline.Create(),
            _options,
            _connection.Group,
            _connection.Customer,
            logger);
    }
}

/// <summary>
/// Owns the Zammad HTTP client and the ticket provider built on it.
/// </summary>
public sealed class ZammadTicketingSession : ITicketingSession
{
    private readonly HttpClient _httpClient;

    public ZammadTicketingSession(
        HttpClient httpClient,
        ZammadOptions options,
        string group,
        string customer,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        TicketProvider =
            new ZammadTicketProvider(
                new ZammadApiClient(httpClient, options, logger),
                group,
                customer);
    }

    public ITicketProvider TicketProvider { get; }

    public void Dispose() => _httpClient.Dispose();
}
