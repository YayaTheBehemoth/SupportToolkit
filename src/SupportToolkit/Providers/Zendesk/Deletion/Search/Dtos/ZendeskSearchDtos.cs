using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zendesk.Deletion.Search.Dtos;

/// <summary>
/// Contract of the read-only search, current-user and lookup endpoints.
/// Values stay nullable: a missing value is unknown, never defaulted.
/// </summary>
public sealed class ZendeskSearchPageDto
{
    [JsonPropertyName("results")]
    public List<ZendeskSearchTicketDto> Results { get; init; } = [];

    [JsonPropertyName("count")]
    public int? Count { get; init; }

    [JsonPropertyName("next_page")]
    public string? NextPage { get; init; }
}

public sealed class ZendeskSearchTicketDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("result_type")]
    public string? ResultType { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("subject")]
    public string? Subject { get; init; }

    [JsonPropertyName("requester_id")]
    public long? RequesterId { get; init; }

    [JsonPropertyName("organization_id")]
    public long? OrganizationId { get; init; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }
}

public sealed class ZendeskCurrentUserDto
{
    [JsonPropertyName("user")]
    public ZendeskUserDto? User { get; init; }
}

public sealed class ZendeskUsersDto
{
    [JsonPropertyName("users")]
    public List<ZendeskUserDto> Users { get; init; } = [];
}

public sealed class ZendeskUserDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

public sealed class ZendeskOrganizationsDto
{
    [JsonPropertyName("organizations")]
    public List<ZendeskOrganizationDto> Organizations { get; init; } = [];
}

public sealed class ZendeskOrganizationDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
