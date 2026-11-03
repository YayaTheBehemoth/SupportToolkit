using System.Security.Cryptography;
using System.Text;

namespace SupportToolkit.Providers.Zendesk.Deletion.Auth;

/// <summary>
/// PKCE (RFC 7636, S256) and anti-forgery state helpers.
/// </summary>
public static class ZendeskPkce
{
    /// <summary>A 256-bit random code verifier, base64url encoded.</summary>
    public static string CreateVerifier() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>A 256-bit random anti-forgery state value.</summary>
    public static string CreateState() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>BASE64URL(SHA256(ASCII(verifier))), no padding.</summary>
    public static string CreateChallenge(
        string verifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifier);

        return Base64Url(
            SHA256.HashData(
                Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64Url(
        byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
