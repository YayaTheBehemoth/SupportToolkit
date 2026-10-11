using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Alerts.Dtos;


/// Represents the top-level Acronis alerts response.
/// This contract mirrors the external API payload and intentionally remains separate from SupportToolkit domain models.

public sealed class AlertPageDto
{
    [JsonPropertyName("items")]
    public required List<AlertDto> Items { get; init; }

    [JsonPropertyName("paging")]
    public required AlertPagingDto Paging { get; init; }
}


/// Represents the paging metadata returned with Acronis alert collections.

public sealed class AlertPagingDto
{
    [JsonPropertyName("cursors")]
    public required Dictionary<string, JsonElement> Cursors { get; init; }
}

public sealed class AlertDto
{
    [JsonPropertyName("_source")]
    public string? Source { get; init; }

    [JsonPropertyName("_sourceTimeStamp")]
    public long? SourceTimeStamp { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("receivedAt")]
    public DateTimeOffset? ReceivedAt { get; init; }

    [JsonPropertyName("tenant")]
    public AlertTenantDto? Tenant { get; init; }


    /// Gets the alert payload details. This is represented as "JsonElement"
    /// because the payload varies by Acronis alert type and is intentionally kept flexible at the transport boundary.

    [JsonPropertyName("details")]
    public required JsonElement Details { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

public sealed class AlertTenantDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("locator")]
    public string? Locator { get; init; }
}
