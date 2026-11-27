using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zammad.Deletion.Dtos;

/// <summary>
/// GET api/v1/tickets/search?full=true: the matching IDs in order, the
/// total, and the records they refer to. Observed against Zammad 7.3.0.
/// </summary>
internal sealed class ZammadSearchResponseDto
{
    [JsonPropertyName("record_ids")]
    public List<long>? RecordIds { get; init; }

    [JsonPropertyName("total_count")]
    public int? TotalCount { get; init; }

    [JsonPropertyName("assets")]
    public ZammadAssetsDto? Assets { get; init; }
}

internal sealed class ZammadAssetsDto
{
    [JsonPropertyName("Ticket")]
    public Dictionary<string, ZammadTicketAssetDto>? Tickets { get; init; }

    [JsonPropertyName("User")]
    public Dictionary<string, ZammadUserAssetDto>? Users { get; init; }

    [JsonPropertyName("Organization")]
    public Dictionary<string, ZammadOrganizationAssetDto>? Organizations { get; init; }
}

internal sealed class ZammadTicketAssetDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("state_id")]
    public long? StateId { get; init; }

    [JsonPropertyName("organization_id")]
    public long? OrganizationId { get; init; }

    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }
}

internal sealed class ZammadUserAssetDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("firstname")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastname")]
    public string? LastName { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role_ids")]
    public List<long>? RoleIds { get; init; }
}

internal sealed class ZammadOrganizationAssetDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>A ticket state or a role: an ID and its name.</summary>
internal sealed class ZammadNamedDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
