using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Tenants.Dtos;

/// <summary>
/// Represents one tenant/group returned by the legacy Account Management API.
///
/// The legacy endpoint exposes both the historical numeric ID and the UUID
/// used by Account Management v2.
/// </summary>
public sealed class LegacyTenantGroupDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }
}

/// <summary>
/// Represents the response returned by the legacy hierarchy children endpoint.
/// </summary>
public sealed class LegacyTenantChildrenPageDto
{
    [JsonPropertyName("items")]
    public required List<LegacyTenantChildDto> Items { get; init; }
}

/// <summary>
/// Represents one child node in the legacy Acronis tenant hierarchy.
/// </summary>
public sealed class LegacyTenantChildDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    /*
     * The legacy API represents this as 0/1 in its documented examples.
     */
    [JsonPropertyName("has_children")]
    public int HasChildren { get; init; }
}
