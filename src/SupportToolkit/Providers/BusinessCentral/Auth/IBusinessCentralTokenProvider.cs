namespace SupportToolkit.Providers.BusinessCentral.Auth;

/// <summary>
/// Supplies delegated Business Central access tokens to the
/// Business Central HTTP pipeline only.
///
/// Tokens are never exposed to modules or provider callers.
/// </summary>
public interface IBusinessCentralTokenProvider
{
    Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default
    );
}
