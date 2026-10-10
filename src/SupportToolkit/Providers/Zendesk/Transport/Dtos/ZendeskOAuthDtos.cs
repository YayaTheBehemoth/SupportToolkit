using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zendesk.Transport.Dtos;

internal sealed class ZendeskClientCredentialsRequestDto
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; init; } =
        "client_credentials";

    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("client_secret")]
    public required string ClientSecret { get; init; }

    [JsonPropertyName("scope")]
    public string Scope { get; init; } =
        "tickets:write";
}

internal sealed class ZendeskOAuthTokenDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}