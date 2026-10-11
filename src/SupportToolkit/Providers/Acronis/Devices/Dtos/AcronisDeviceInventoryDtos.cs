using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Devices.Dtos;

public sealed class AcronisDeviceResourcePageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisDeviceResourceDto> Items { get; init; } = [];

    [JsonPropertyName("paging")]
    public AcronisDevicePagingDto Paging { get; init; } = new();
}

public sealed class AcronisDevicePagingDto
{
    [JsonPropertyName("cursors")]
    public AcronisDeviceCursorDto Cursors { get; init; } = new();
}

public sealed class AcronisDeviceCursorDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}

public sealed class AcronisDeviceResourceDto
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
    public AcronisDeviceResourceStatusDto? Status { get; init; }

    [JsonPropertyName("agent")]
    public AcronisDeviceAgentDto? Agent { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties { get; init; } = [];
}

public sealed class AcronisDeviceResourceStatusDto
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
    public Dictionary<string, JsonElement> AdditionalProperties { get; init; } = [];
}

public sealed class AcronisDeviceAgentDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
