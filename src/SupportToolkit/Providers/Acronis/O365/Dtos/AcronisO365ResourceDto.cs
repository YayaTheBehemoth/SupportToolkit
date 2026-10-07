using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.O365.Dtos;

public sealed class AcronisO365ResourceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("internalId")]
    public string? InternalId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; init; }

    [JsonPropertyName("basicKinds")]
    public IReadOnlyList<AcronisO365BasicKindDto> BasicKinds { get; init; } =
        [];

    [JsonPropertyName("hasProtections")]
    public bool? HasProtections { get; init; }

    [JsonPropertyName("hasCompositeProtections")]
    public bool? HasCompositeProtections { get; init; }

    [JsonPropertyName("online")]
    public bool? Online { get; init; }

    [JsonPropertyName("mailboxOnline")]
    public bool? MailboxOnline { get; init; }

    [JsonPropertyName("lastStartTime")]
    public DateTimeOffset? LastStartTime { get; init; }

    [JsonPropertyName("lastFinishTime")]
    public DateTimeOffset? LastFinishTime { get; init; }

    [JsonPropertyName("lastSuccessTime")]
    public DateTimeOffset? LastSuccessTime { get; init; }

    [JsonPropertyName("nextStartTime")]
    public DateTimeOffset? NextStartTime { get; init; }

    [JsonPropertyName("lastTaskState")]
    public string? LastTaskState { get; init; }

    [JsonPropertyName("lastTaskStatus")]
    public string? LastTaskStatus { get; init; }

    [JsonPropertyName("lastTaskProgress")]
    public int? LastTaskProgress { get; init; }

    [JsonPropertyName("recipientType")]
    public string? RecipientType { get; init; }

    [JsonPropertyName("applications")]
    public IReadOnlyList<JsonElement> Applications { get; init; } =
        [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisO365BasicKindDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("hasProtections")]
    public bool? HasProtections { get; init; }
}