using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zendesk.Deletion.Auth;

/// <summary>
/// Authorization code exchange for a public client. There is no
/// client_secret property by design: none is ever sent.
/// </summary>
internal sealed class ZendeskPkceTokenRequestDto
{
    [JsonPropertyName("grant_type")]
    public string GrantType { get; init; } =
        "authorization_code";

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("redirect_uri")]
    public required string RedirectUri { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("code_verifier")]
    public required string CodeVerifier { get; init; }
}

/// <summary>
/// ExpiresIn is nullable: a missing lifetime is unknown, never assumed.
/// A refresh token, if returned, is intentionally not modelled and
/// therefore never read or kept.
/// </summary>
internal sealed class ZendeskPkceTokenResponseDto
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}
