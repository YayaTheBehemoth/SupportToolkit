using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.O365.Dtos;

public sealed class AcronisO365GroupsDto
{
    /*
     * The O365 Resource Manager UI uses "data" for group collections.
     * "items" is retained defensively because nearby Acronis APIs use it.
     */
    [JsonPropertyName("data")]
    public IReadOnlyList<AcronisO365GroupDto> Data
    {
        get;
        init;
    } = [];

    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisO365GroupDto> Items
    {
        get;
        init;
    } = [];

    [JsonIgnore]
    public IReadOnlyList<AcronisO365GroupDto> Groups =>
        Data.Count > 0
            ? Data
            : Items;
}

public sealed class AcronisO365GroupDto
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
    public IReadOnlyList<string> ResourceTypes
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