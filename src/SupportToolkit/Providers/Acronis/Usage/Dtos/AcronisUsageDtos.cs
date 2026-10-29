using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Usage.Dtos;

/// <summary>
/// Response of the read-only batch usage endpoint
/// (GET /api/2/tenants/usages).
///
/// The shape follows Acronis's published contract and has not been
/// validated against production. Values stay nullable: a missing
/// value is unknown, never zero.
/// </summary>
public sealed class UsagePageDto
{
    [JsonPropertyName("items")]
    public List<TenantUsageDto> Items { get; init; } = [];
}

public sealed class TenantUsageDto
{
    [JsonPropertyName("tenant")]
    public string? Tenant { get; init; }

    [JsonPropertyName("usages")]
    public List<UsageDto> Usages { get; init; } = [];
}

public sealed class UsageDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("usage_name")]
    public string? UsageName { get; init; }

    [JsonPropertyName("edition")]
    public string? Edition { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("application_id")]
    public string? ApplicationId { get; init; }

    [JsonPropertyName("infra_id")]
    public string? InfraId { get; init; }

    [JsonPropertyName("measurement_unit")]
    public string? MeasurementUnit { get; init; }

    [JsonPropertyName("value")]
    public decimal? Value { get; init; }

    [JsonPropertyName("absolute_value")]
    public decimal? AbsoluteValue { get; init; }

    [JsonPropertyName("range_start")]
    public string? RangeStart { get; init; }

    /*
     * Unknown fields are retained so a contract difference is visible
     * instead of silently dropped. Only names may be summarized.
     */
    [JsonExtensionData]
    public Dictionary<string, JsonElement> AdditionalProperties
    {
        get;
        set;
    } = [];
}
