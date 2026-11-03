namespace SupportToolkit.Providers.Zendesk.Deletion.Auth;

/// <summary>
/// An interactive Zendesk sign-in did not complete or has expired.
/// Messages carry no token, code or response body.
/// </summary>
public sealed class ZendeskAuthenticationException : Exception
{
    public ZendeskAuthenticationException(string message)
        : base(message)
    {
    }

    public ZendeskAuthenticationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}
