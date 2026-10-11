using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Tenants.Dtos;

/// <summary>
/// Represents the top-level Acronis tenant response.
///
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
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /*
     * Production validation showed that resource-management tenant IDs are
     * numeric while Account Management tenant IDs are UUIDs.
     *
     * customer_id was investigated as a possible cross-API identifier, but
     * production currently returns it as absent for all observed tenants.
     */
    [JsonPropertyName("customer_id")]
    public string? CustomerId { get; init; }

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

    /*
     * Unknown production fields are retained instead of silently discarded.
     *
     * This is primarily useful while validating Acronis's real tenant
     * contract against the documented contract. Values are never written to
     * operational logs; only field names and JSON value kinds may be
     * summarized.
     */
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        set;
    } = [];
}
