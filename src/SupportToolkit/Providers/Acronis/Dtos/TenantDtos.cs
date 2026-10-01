using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Dtos;

/// <summary>
/// Represents the top-level Acronis tenant response.
/// This contract mirrors the external API payload and intentionally remains
/// separate from SupportToolkit domain models.
/// </summary>
public sealed class TenantPageDto
{
    [JsonPropertyName("paging")]
    public required TenantPagingDto Paging { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("items")]
    public required List<TenantDto> Items { get; init; }
}

/// <summary>
/// Represents paging metadata returned with an Acronis tenant collection.
/// </summary>
public sealed class TenantPagingDto
{
    [JsonPropertyName("cursors")]
    public required TenantPagingCursorsDto Cursors { get; init; }
}

public sealed class TenantPagingCursorsDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }
}

public sealed class TenantDto
{
    /*
     * Id and Name are the only tenant fields BackupHealth fundamentally
     * requires for resource-to-customer correlation.
     */
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /*
     * Remaining fields describe the external account structure but should
     * not prevent an otherwise usable tenant from being deserialized.
     */
    [JsonPropertyName("parent_id")]
    public string? ParentId { get; init; }

    [JsonPropertyName("version")]
    public int? Version { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("contacts")]
    public List<JsonElement> Contacts { get; init; } = [];

    [JsonPropertyName("offering_items")]
    public List<JsonElement> OfferingItems { get; init; } = [];
}