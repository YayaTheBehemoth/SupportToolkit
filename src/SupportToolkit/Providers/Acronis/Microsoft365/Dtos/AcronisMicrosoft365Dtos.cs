
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

public sealed class AcronisMicrosoft365GroupsDto
{
    private IReadOnlyList<AcronisMicrosoft365GroupDto?>? _data;
    private IReadOnlyList<AcronisMicrosoft365GroupDto?>? _items;

    /*
     * An explicitly null collection is different from a missing property.
     *
     * Acronis has been observed returning null for an unconfigured
     * Microsoft 365 inventory, so retain that supported behavior.
     */
    [JsonPropertyName("data")]
    public IReadOnlyList<AcronisMicrosoft365GroupDto?>? Data
    {
        get => _data;
        init
        {
            _data = value;
            HasCollectionProperty = true;
        }
    }

    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisMicrosoft365GroupDto?>? Items
    {
        get => _items;
        init
        {
            _items = value;
            HasCollectionProperty = true;
        }
    }

    [JsonIgnore]
    public bool HasCollectionProperty { get; private set; }

    [JsonIgnore]
    public IReadOnlyList<AcronisMicrosoft365GroupDto> Groups
    {
        get
        {
            var source =
                Data is { Count: > 0 }
                    ? Data
                    : Items;

            if (source is null || source.Count == 0)
            {
                return [];
            }

            return source
                .Where(group => group is not null)
                .Select(group => group!)
                .ToList()
                .AsReadOnly();
        }
    }
}

public sealed class AcronisMicrosoft365GroupDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; init; }

    [JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    [JsonPropertyName("groupKind")]
    public string? GroupKind { get; init; }

    [JsonPropertyName("groupType")]
    public int? GroupType { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("leaf")]
    public bool? Leaf { get; init; }

    [JsonPropertyName("custom")]
    public bool? Custom { get; init; }

    [JsonPropertyName("resource_types")]
    public IReadOnlyList<string>? ResourceTypes { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties
    {
        get;
        init;
    }
}

public sealed class AcronisMicrosoft365ResourcePageDto
{
    private IReadOnlyList<AcronisMicrosoft365ResourceDto?>? _items;

    /*
     * Explicit null and empty arrays are supported.
     * A missing items property is instead an incomplete response.
     */
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisMicrosoft365ResourceDto?>? Items
    {
        get => _items;
        init
        {
            _items = value;
            HasItemsProperty = true;
        }
    }

    [JsonIgnore]
    public bool HasItemsProperty { get; private set; }

    [JsonPropertyName("paging")]
    public AcronisMicrosoft365PagingDto? Paging { get; init; }

    [JsonPropertyName("timeStamp")]
    public long? TimeStamp { get; init; }
}

public sealed class AcronisMicrosoft365PagingDto
{
    [JsonPropertyName("cursors")]
    public AcronisMicrosoft365CursorDto? Cursors { get; init; }
}

public sealed class AcronisMicrosoft365CursorDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}

public sealed class AcronisMicrosoft365ResourceDto
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

    /*
     * Observed in production on Microsoft 365 GROUP resources.
     * Identifies the associated Team displayed in Acronis's Team column.
     *
     * Association alone does not establish workload duplication.
     */
    [JsonPropertyName("teamName")]
    public string? TeamName { get; init; }

    [JsonPropertyName("basicKinds")]
    public IReadOnlyList<AcronisMicrosoft365BasicKindDto?>? BasicKinds
    {
        get;
        init;
    }

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
    public IReadOnlyList<JsonElement>? Applications { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties
    {
        get;
        init;
    }
}

public sealed class AcronisMicrosoft365BasicKindDto
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("hasProtections")]
    public bool? HasProtections { get; init; }
}
