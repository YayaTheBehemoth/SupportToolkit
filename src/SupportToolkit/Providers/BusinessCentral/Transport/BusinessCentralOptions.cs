using System.Text.RegularExpressions;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// Validated runtime configuration for the Business Central API.
///
/// Security-relevant values are pinned in code rather than taken from
/// configuration: the API host, the authority host and the delegated
/// scope. A tampered configuration file therefore cannot redirect a
/// bearer token to another host.
///
/// No secret is held here. The integration uses a public client with
/// delegated sign-in.
/// </summary>
public sealed class BusinessCentralOptions
{
    public const string ApiHost =
        "api.businesscentral.dynamics.com";

    public const string AuthorityHost =
        "login.microsoftonline.com";

    /// <summary>
    /// Delegated scope requested at sign-in. Confirm against the
    /// tenant's app registration before production use.
    /// </summary>
    public const string DelegatedScope =
        "https://api.businesscentral.dynamics.com/user_impersonation";

    /// <summary>
    /// Fixed request language. Some columns (such as subscription
    /// status) are language dependent, so the language is pinned
    /// rather than inherited from the signed-in user.
    /// </summary>
    public const string RequestLanguage =
        "en-US";

    private static readonly Regex SegmentPattern =
        new(
            @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant
        );

    public required string TenantId { get; init; }

    public required string ClientId { get; init; }

    public required string Environment { get; init; }

    public required string CompanyId { get; init; }

    public required string ApiPublisher { get; init; }

    public required string ApiGroup { get; init; }

    public required string ApiVersion { get; init; }

    /// <summary>
    /// Company name as reported in row-level company columns. Used
    /// only to validate source context; the company ID in the URL
    /// remains the authoritative selector.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Tenant-specific authority. The multi-tenant "common" and
    /// "organizations" authorities are never used.
    /// </summary>
    public Uri AuthorityUri =>
        new(
            $"https://{AuthorityHost}/{TenantId}/"
        );

    public Uri ApiBaseUri =>
        new(
            $"https://{ApiHost}/v2.0/{TenantId}/{Environment}" +
            $"/api/{ApiPublisher}/{ApiGroup}/{ApiVersion}/"
        );

    /// <summary>
    /// Relative path prefix of the configured company.
    /// </summary>
    public string CompanyPath =>
        $"companies({CompanyId})";

    public static BusinessCentralOptions Create(
        string tenantId,
        string clientId,
        string environment,
        string companyId,
        string apiPublisher,
        string apiGroup,
        string apiVersion,
        string? companyName = null)
    {
        return new BusinessCentralOptions
        {
            CompanyName =
                RequireOptionalName(
                    companyName,
                    "Business Central company name"
                ),

            TenantId =
                RequireGuid(
                    tenantId,
                    "Business Central tenant ID"
                ),

            ClientId =
                RequireGuid(
                    clientId,
                    "Business Central client ID"
                ),

            Environment =
                RequireSegment(
                    environment,
                    "Business Central environment"
                ),

            CompanyId =
                RequireGuid(
                    companyId,
                    "Business Central company ID"
                ),

            ApiPublisher =
                RequireSegment(
                    apiPublisher,
                    "Business Central API publisher"
                ),

            ApiGroup =
                RequireSegment(
                    apiGroup,
                    "Business Central API group"
                ),

            ApiVersion =
                RequireSegment(
                    apiVersion,
                    "Business Central API version"
                )
        };
    }

    private static string? RequireOptionalName(
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed =
            value.Trim();

        if (trimmed.Length > 100
            || trimmed.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"{description} is not valid."
            );
        }

        return trimmed;
    }

    private static string RequireGuid(
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{description} is not configured."
            );
        }

        if (!Guid.TryParse(
                value.Trim(),
                out var guid))
        {
            throw new InvalidOperationException(
                $"{description} must be a GUID."
            );
        }

        return guid.ToString("D");
    }

    /*
     * Values become URL path segments. Restricting them to a
     * conservative character set prevents traversal, query injection
     * and host manipulation through configuration.
     */
    private static string RequireSegment(
        string? value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{description} is not configured."
            );
        }

        var trimmed =
            value.Trim();

        if (!SegmentPattern.IsMatch(trimmed)
            || trimmed.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{description} contains unsupported characters."
            );
        }

        return trimmed;
    }
}
