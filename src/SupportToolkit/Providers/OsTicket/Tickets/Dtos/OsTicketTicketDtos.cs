using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.OsTicket.Tickets.Dtos;

/// <summary>
/// The JSON body of POST api/tickets.json. Observed against osTicket
/// 1.17.5; see lab/osticket/README.md.
/// </summary>
internal sealed class OsTicketCreateTicketRequestDto
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>
    /// A data URL, so osTicket stores the body as plain text (HTML is
    /// escaped) whatever it contains.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("topicId")]
    public required int TopicId { get; init; }

    /// <summary>No alert to staff: the ticket is the notification.</summary>
    [JsonPropertyName("alert")]
    public bool Alert { get; init; }

    /// <summary>No auto-reply to the requester.</summary>
    [JsonPropertyName("autorespond")]
    public bool AutoRespond { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "API";
}
