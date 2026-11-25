using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zammad.Tickets.Dtos;

/// <summary>
/// The JSON body of POST api/v1/tickets. Observed against Zammad 7.3.0;
/// see lab/zammad/README.md.
/// </summary>
internal sealed class ZammadCreateTicketRequestDto
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>The group, by name; an unknown name is refused (422).</summary>
    [JsonPropertyName("group")]
    public required string Group { get; init; }

    /// <summary>
    /// "guess:" and an email address: Zammad uses that customer, and
    /// creates it on first use.
    /// </summary>
    [JsonPropertyName("customer_id")]
    public required string Customer { get; init; }

    [JsonPropertyName("article")]
    public required ZammadArticleDto Article { get; init; }
}

internal sealed class ZammadArticleDto
{
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>Stored exactly as sent, as plain text.</summary>
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; init; } = "text/plain";

    /// <summary>A note sends no email to the customer.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "note";

    [JsonPropertyName("internal")]
    public bool Internal { get; init; }
}

/// <summary>The parts of a created ticket SupportToolkit reads.</summary>
internal sealed class ZammadTicketDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("number")]
    public string? Number { get; init; }
}

/// <summary>Zammad's error body: {"error": …, "error_human": …}.</summary>
internal sealed class ZammadErrorDto
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("error_human")]
    public string? ErrorHuman { get; init; }
}
