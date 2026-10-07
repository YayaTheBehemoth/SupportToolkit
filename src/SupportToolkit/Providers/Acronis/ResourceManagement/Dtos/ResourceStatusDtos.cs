using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;

/// <summary>
/// Represents the top-level Acronis resource status response.
/// This contract mirrors the external API payload and intentionally remains
/// separate from SupportToolkit domain models.
/// </summary>
public sealed class ResourceStatusPageDto
{
    [JsonPropertyName("paging")]
    public required PagingDto Paging { get; init; }

    [JsonPropertyName("items")]
    public required List<ResourceStatusDto> Items { get; init; }

    /*
     * Acronis production responses do not consistently include the
     * top-level timestamp field, and BackupHealth does not depend on it.
     *
     * Keep it optional so an otherwise valid page remains usable.
     */
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }
}

public sealed class PagingDto
{
    [JsonPropertyName("cursors")]
    public required PagingCursorsDto Cursors { get; init; }
}

public sealed class PagingCursorsDto
{
    [JsonPropertyName("before")]
    public string? Before { get; init; }

    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

public sealed class ResourceStatusDto
{
    [JsonPropertyName("context")]
    public required ResourceDto Context { get; init; }

    [JsonPropertyName("aggregate")]
    public AggregateStatusDto? Aggregate { get; init; }

    [JsonPropertyName("licensing")]
    public LicensingStatusDto? Licensing { get; init; }

    [JsonPropertyName("policies")]
    public List<PolicyExecutionDto>? Policies { get; init; }
}

public sealed class ResourceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("user_defined_name")]
    public string? UserDefinedName { get; init; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /*
     * These fields are useful transport metadata but are not required by
     * BackupHealth. Keeping them optional prevents an otherwise valid
     * resource from failing deserialization when Acronis omits them.
     */
    [JsonPropertyName("cti")]
    public string? Cti { get; init; }

    [JsonPropertyName("parent_group_ids")]
    public List<string> ParentGroupIds { get; init; } = [];

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("deleted_at")]
    public DateTimeOffset? DeletedAt { get; init; }
}

public sealed class AggregateStatusDto
{
    /*
     * Acronis does not necessarily include running-state metadata when a
     * resource is idle, so aggregate status must remain usable without it.
     */
    [JsonPropertyName("running")]
    public RunningStatusDto? Running { get; init; }

    [JsonPropertyName("names")]
    public string? Names { get; init; }

    /// <summary>
    /// Gets the external Acronis status value. This remains a string so
    /// previously unknown status values can deserialize without breaking
    /// the integration.
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

public sealed class RunningStatusDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("activities")]
    public List<string>? Activities { get; init; }

    [JsonPropertyName("progress")]
    public int? Progress { get; init; }
}

public sealed class LicensingStatusDto
{
    [JsonPropertyName("current_offering_item")]
    public string? CurrentOfferingItem { get; init; }

    [JsonPropertyName("previous_offering_item")]
    public string? PreviousOfferingItem { get; init; }
}

public sealed class PolicyExecutionDto
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("last_run_time")]
    public DateTimeOffset? LastRunTime { get; init; }

    [JsonPropertyName("last_success_run_time")]
    public DateTimeOffset? LastSuccessRunTime { get; init; }

    [JsonPropertyName("next_run_time")]
    public DateTimeOffset? NextRunTime { get; init; }

    [JsonPropertyName("next_enable_time")]
    public DateTimeOffset? NextEnableTime { get; init; }

    [JsonPropertyName("run_events")]
    public List<string>? RunEvents { get; init; }

    [JsonPropertyName("enable_events")]
    public List<string>? EnableEvents { get; init; }
}
