using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Dtos;

public sealed class AcronisActivityDto
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; init; }

    [JsonPropertyName("idString")]
    public string? Id { get; init; }

    /*
     * Acronis exposes an opaque GUID-like activity type here.
     * Do not use this field for domain classification.
     */
    [JsonPropertyName("type")]
    public string? TypeId { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; init; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    [JsonPropertyName("startedByUser")]
    public string? StartedByUser { get; init; }

    [JsonPropertyName("executor")]
    public AcronisActivityExecutorDto? Executor { get; init; }

    [JsonPropertyName("policy")]
    public AcronisActivityPolicyDto? Policy { get; init; }

    [JsonPropertyName("resource")]
    public AcronisActivityResourceDto? Resource { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } =
        [];

    [JsonPropertyName("taskId")]
    public long? TaskId { get; init; }

    [JsonPropertyName("taskIdString")]
    public string? TaskIdString { get; init; }

    [JsonPropertyName("context")]
    public AcronisActivityContextDto Context { get; init; } =
        new();

    [JsonPropertyName("result")]
    public AcronisActivityResultDto Result { get; init; } =
        new();

    [JsonPropertyName("tenant")]
    public AcronisActivityTenantDto Tenant { get; init; } =
        new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisActivityExecutorDto
{
    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

public sealed class AcronisActivityPolicyDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class AcronisActivityResourceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

public sealed class AcronisActivityContextDto
{
    [JsonPropertyName("activityType")]
    public string? ActivityType { get; init; }

    [JsonPropertyName("archiveDisplayName")]
    public string? ArchiveDisplayName { get; init; }

    [JsonPropertyName("archiveName")]
    public string? ArchiveName { get; init; }

    [JsonPropertyName("policyId")]
    public string? PolicyId { get; init; }

    [JsonPropertyName("policyName")]
    public string? PolicyName { get; init; }

    [JsonPropertyName("resourceId")]
    public string? ResourceId { get; init; }

    [JsonPropertyName("resourceKind")]
    public string? ResourceKind { get; init; }

    [JsonPropertyName("resourceName")]
    public string? ResourceName { get; init; }

    [JsonPropertyName("resourceSubtype")]
    public string? ResourceSubtype { get; init; }

    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisActivityResultDto
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /*
     * Payload remains raw for now.
     *
     * We know backup types expose useful diagnostics here, but the exact
     * payload shape varies by activity type.
     */
    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisActivityTenantDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("locator")]
    public string? Locator { get; init; }
}