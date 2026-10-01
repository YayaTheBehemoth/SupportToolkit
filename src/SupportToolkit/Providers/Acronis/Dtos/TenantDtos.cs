using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Dtos;


/// Represents the top-level Acronis tenant response.
/// This contract mirrors the external API payload and intentionally remains separate from SupportToolkit domain models.

public sealed class TenantPageDto
{
    [JsonPropertyName("paging")]
    public required TenantPagingDto Paging { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("items")]
    public required List<TenantDto> Items { get; init; }
}


/// Represents the paging metadata returned with an Acronis tenant collection.

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
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("parent_id")]
    public required string ParentId { get; init; }

    [JsonPropertyName("version")]
    public required int Version { get; init; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; init; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("enabled")]
    public required bool Enabled { get; init; }

    [JsonPropertyName("contacts")]
    public required List<JsonElement> Contacts { get; init; }

    [JsonPropertyName("offering_items")]
    public required List<JsonElement> OfferingItems { get; init; }
}