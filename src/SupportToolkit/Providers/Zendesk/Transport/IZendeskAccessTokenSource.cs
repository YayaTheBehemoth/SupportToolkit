namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// Supplies a Zendesk OAuth access token to the transport that owns one
/// Zendesk connection.
///
/// Each connection has its own source, so credentials, scopes and token
/// lifetimes are never shared between connections that serve different
/// functions. Implementations keep tokens in memory only, and callers
/// never log or expose them.
/// </summary>
public interface IZendeskAccessTokenSource
{
    Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default);
}
