using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.O365.Dtos;

public sealed class AcronisO365ApplicationsDto
{
    [JsonPropertyName("applications")]
    public IReadOnlyList<AcronisO365ApplicationDto> Applications
    {
        get;
        init;
    } = [];
}

public sealed class AcronisO365ApplicationDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("applicationId")]
    public string? ApplicationId { get; init; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; init; }

    [JsonPropertyName("suite")]
    public string? Suite { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("isDefault")]
    public bool? IsDefault { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        init;
    } = [];
}

public sealed class AcronisO365GroupsDto
{
    /*
     * The production UI has shown group responses using "data".
     * "items" is retained defensively because nearby Acronis endpoints
     * use that collection name extensively.
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