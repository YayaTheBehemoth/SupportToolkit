namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// Validation shared by every Zendesk connection type, so a subdomain
/// can never be used to point a request at another host.
/// </summary>
internal static class ZendeskSubdomain
{
    public static void Validate(
        string subdomain)
    {
        if (subdomain.Length > 63
            || subdomain.StartsWith('-')
            || subdomain.EndsWith('-')
            || subdomain.Any(
                character =>
                    !IsAsciiLetterOrDigit(
                        character
                    )
                    && character != '-'
            ))
        {
            throw new InvalidOperationException(
                "Zendesk subdomain must contain only letters, " +
                "numbers, and hyphens."
            );
        }
    }

    private static bool IsAsciiLetterOrDigit(
        char character)
    {
        return character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9';
    }
}
