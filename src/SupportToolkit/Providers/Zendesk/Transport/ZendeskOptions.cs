namespace SupportToolkit.Providers.Zendesk.Transport;

/// <summary>
/// Runtime configuration required to authenticate against a Zendesk instance.
///
/// Resolution of configuration is handled outside the provider. This class
/// owns validation of the final Zendesk-specific values.
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

    public static ZendeskOptions Create(
        string subdomain,
        string clientId,
        string clientSecret)
    {
        subdomain =
            RequireValue(
                subdomain,
                "Zendesk subdomain"
            );

        clientId =
            RequireValue(
                clientId,
                "Zendesk client ID"
            );

        clientSecret =
            RequireValue(
                clientSecret,
                "Zendesk client secret"
            );

        ValidateSubdomain(
            subdomain
        );

        return new ZendeskOptions
        {
            Subdomain =
                subdomain,

            ClientId =
                clientId,

            ClientSecret =
                clientSecret
        };
    }

    public static ZendeskOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        return Create(
            RequireEnvironmentVariable(
                environmentReader,
                "ZENDESK_SUBDOMAIN"
            ),
            RequireEnvironmentVariable(
                environmentReader,
                "ZENDESK_CLIENT_ID"
            ),
            RequireEnvironmentVariable(
                environmentReader,
                "ZENDESK_CLIENT_SECRET"
            )
        );
    }

    private static string RequireEnvironmentVariable(
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

    private static string RequireValue(
        string value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"{description} is not configured."
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