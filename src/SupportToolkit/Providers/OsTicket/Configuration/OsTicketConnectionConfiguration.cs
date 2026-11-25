using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Providers.OsTicket.Configuration;

/// <summary>
/// Non-secret osTicket connection settings, under connections.osTicket in
/// the configuration file.
///
/// BaseUrl is the helpdesk root (the folder holding api/ and scp/).
/// RequesterName and RequesterEmail identify who tickets are opened for;
/// osTicket creates that user on first use. The API key is a secret and
/// lives in the secret store.
/// </summary>
public sealed class OsTicketConnectionConfiguration : ConfigurationNode
{
    public string BaseUrl { get; set; } =
        string.Empty;

    public string RequesterName { get; set; } =
        string.Empty;

    public string RequesterEmail { get; set; } =
        string.Empty;

    public Dictionary<string, OsTicketDestinationConfiguration> Destinations
    {
        get;
        set;
    } = new();
}

/// <summary>
/// A named ticket destination within one osTicket instance: a help topic.
/// osTicket accepts an unknown topic without error (the ticket lands in
/// the default department), so a positive ID is all that can be checked.
/// </summary>
public sealed class OsTicketDestinationConfiguration : ConfigurationNode
{
    public int TopicId { get; set; }
}

/// <summary>
/// An osTicket connection and destination, validated locally before any
/// osTicket request.
/// </summary>
public sealed record ResolvedOsTicketConnection(
    string ConnectionName,
    string BaseUrl,
    string ApiKey,
    string RequesterName,
    string RequesterEmail,
    string DestinationName,
    int TopicId);
