namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// Runtime configuration required to authenticate against a Zendesk instance.
/// Secrets must be supplied at runtime and never committed to source control.
/// </summary>
public sealed class ZendeskOptions
{
    public required string Subdomain { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public Uri BaseUri =>
        new(
            $"https://{Subdomain}.zendesk.com/",
            UriKind.Absolute
        );

    public static ZendeskOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        var subdomain =
            Require(
                environmentReader,
                "ZENDESK_SUBDOMAIN"
            );

        ValidateSubdomain(
            subdomain
        );

        return new ZendeskOptions
        {
            Subdomain =
                subdomain,

            ClientId =
                Require(
                    environmentReader,
                    "ZENDESK_CLIENT_ID"
                ),

            ClientSecret =
                Require(
                    environmentReader,
                    "ZENDESK_CLIENT_SECRET"
                )
        };
    }

    private static string Require(
        Func<string, string?> environmentReader,
        string variableName)
    {
        var value =
            environmentReader(
                variableName
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"Required environment variable " +
                $"'{variableName}' is not configured."
            );
        }

        return value.Trim();
    }

    private static void ValidateSubdomain(
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
                "ZENDESK_SUBDOMAIN must contain only " +
                "letters, numbers, and hyphens."
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