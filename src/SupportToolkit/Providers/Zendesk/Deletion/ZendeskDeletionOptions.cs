using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Providers.Zendesk.Deletion;

/// <summary>
/// Runtime configuration of the Zendesk deletion connection: a public
/// OAuth client that signs in interactively with PKCE.
///
/// There is deliberately no client secret. This is a different type from
/// ZendeskOptions, which belongs to the ticketing connection and requires
/// one, so the two connections cannot be mixed up or share credentials.
/// </summary>
public sealed class ZendeskDeletionOptions
{
    /// <summary>
    /// Scopes the sign-in requests. "read" is needed for search and the
    /// signed-in user's identity; "tickets:write" is needed for the
    /// later deletion step. "tickets:read" alone is not enough.
    /// </summary>
    public const string SignInScope = "read tickets:write";

    public required string Subdomain { get; init; }

    public required string ClientId { get; init; }

    public Uri BaseUri =>
        new(
            $"https://{Subdomain}.zendesk.com/",
            UriKind.Absolute
        );

    public static ZendeskDeletionOptions Create(
        string subdomain,
        string clientId)
    {
        subdomain =
            RequireValue(
                subdomain,
                "Zendesk deletion subdomain"
            );

        clientId =
            RequireValue(
                clientId,
                "Zendesk deletion client ID"
            );

        ZendeskSubdomain.Validate(
            subdomain
        );

        return new ZendeskDeletionOptions
        {
            Subdomain =
                subdomain,

            ClientId =
                clientId
        };
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
}
