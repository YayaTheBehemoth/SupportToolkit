using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Epm.Dtos;

public sealed class AcronisEpmGroupPageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisEpmGroupDto> Items { get; init; } = [];

    [JsonPropertyName("data")]
    public IReadOnlyList<AcronisEpmGroupDto> Data { get; init; } = [];

    [JsonIgnore]
    public IReadOnlyList<AcronisEpmGroupDto> Groups =>
        Items.Count > 0
            ? Items
            : Data;
}

public sealed class AcronisEpmGroupDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("details")]
    public AcronisEpmGroupDetailsDto? Details { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisEpmGroupDetailsDto
{
    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("leaf")]
    public bool? Leaf { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata
    {
        get;
        init;
    } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisEpmResourcePageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisEpmResourceDto> Items { get; init; } = [];

    [JsonPropertyName("paging")]
    public AcronisEpmPagingDto Paging { get; init; } = new();
}

public sealed class AcronisEpmPagingDto
{
    [JsonPropertyName("cursors")]
    public AcronisEpmCursorDto Cursors { get; init; } = new();
}

public sealed class AcronisEpmCursorDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}

public sealed class AcronisEpmResourceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("status")]
    public AcronisEpmResourceStatusDto? Status { get; init; }

    [JsonPropertyName("agent")]
    public AcronisEpmAgentDto? Agent { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisEpmResourceStatusDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("appliedPolicyNames")]
    public string? AppliedPolicyNames { get; init; }

    [JsonPropertyName("lastAction")]
    public string? LastAction { get; init; }

    [JsonPropertyName("lastActionTime")]
    public DateTimeOffset? LastActionTime { get; init; }

    [JsonPropertyName("lastBackup")]
    public DateTimeOffset? LastBackup { get; init; }

    [JsonPropertyName("lastSuccessActionTime")]
    public DateTimeOffset? LastSuccessActionTime { get; init; }

    [JsonPropertyName("lastSuccessBackup")]
    public DateTimeOffset? LastSuccessBackup { get; init; }

    [JsonPropertyName("nextBackup")]
    public DateTimeOffset? NextBackup { get; init; }

    [JsonPropertyName("nextBackupPolicy")]
    public string? NextBackupPolicy { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisEpmAgentDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}