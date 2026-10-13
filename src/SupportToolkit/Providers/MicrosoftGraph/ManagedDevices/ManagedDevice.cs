
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

public sealed class ManagedDevice
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; init; }

    [JsonPropertyName("operatingSystem")]
    public string? OperatingSystem { get; init; }

    [JsonPropertyName("complianceState")]
    public string? ComplianceState { get; init; }

    [JsonPropertyName("lastSyncDateTime")]
    public DateTimeOffset? LastSyncDateTime { get; init; }
}
