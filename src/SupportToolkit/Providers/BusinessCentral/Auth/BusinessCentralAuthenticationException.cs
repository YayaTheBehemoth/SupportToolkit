namespace SupportToolkit.Providers.BusinessCentral.Auth;

/// <summary>
/// Sign-in failure with an operator-safe message.
///
/// Messages never contain MSAL error text, claims, account names or
/// tenant identifiers.
/// </summary>
public sealed class BusinessCentralAuthenticationException
    : InvalidOperationException
{
    public BusinessCentralAuthenticationException(
        string message,
        Exception? innerException = null)
        : base(
            message,
            innerException)
    {
    }
}
