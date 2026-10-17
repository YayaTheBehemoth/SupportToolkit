
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Tenants.Dtos;

/// <summary>
/// Represents the legacy Account Management root group.
///
/// The legacy endpoint exposes both the historical numeric ID
/// and the UUID used by Account Management v2.
/// </summary>
public sealed class LegacyTenantGroupDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }
}

/// <summary>
/// Represents a legacy hierarchy children response.
/// </summary>
public sealed class LegacyTenantChildrenPageDto
{
    /*
     * Required causes missing 'items' to fail deserialization.
     *
     * Explicit null is rejected by the provider.
     * An empty array is valid.
     */
    [JsonPropertyName("items")]
    public required List<LegacyTenantChildDto> Items { get; init; }
}

/// <summary>
/// Represents a child of a legacy Acronis group.
/// </summary>
public sealed class LegacyTenantChildDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    /*
     * Production observation:
     * All 39 inspected hierarchy children provided this field
     * as a JSON number with value zero.
     *
     * JsonRequired prevents a missing property from silently
     * becoming zero during deserialization.
     *
     * A null or structurally incompatible value fails decoding.
     */
    [JsonRequired]
    [JsonPropertyName("has_children")]
    public int HasChildren { get; init; }
}
