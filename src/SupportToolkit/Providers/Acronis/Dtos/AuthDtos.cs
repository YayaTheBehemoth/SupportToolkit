using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Dtos;

public sealed class AcronisTokenDto
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("expires_on")]
    public required long ExpiresOn { get; init; }
}

public sealed class AcronisClientDto
{
    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}