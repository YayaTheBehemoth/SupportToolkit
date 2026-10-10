using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zendesk.Tickets.Dtos;

internal sealed class ZendeskCreateTicketRequestDto
{
    [JsonPropertyName("ticket")]
    public required ZendeskCreateTicketDto Ticket { get; init; }
}

internal sealed class ZendeskCreateTicketDto
{
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    [JsonPropertyName("comment")]
    public required ZendeskTicketCommentDto Comment { get; init; }
}

internal sealed class ZendeskTicketCommentDto
{
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("public")]
    public bool Public { get; init; }
}

internal sealed class ZendeskCreateTicketResponseDto
{
    [JsonPropertyName("ticket")]
    public ZendeskCreatedTicketDto? Ticket { get; init; }
}

internal sealed class ZendeskCreatedTicketDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}