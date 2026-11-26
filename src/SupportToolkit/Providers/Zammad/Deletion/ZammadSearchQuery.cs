namespace SupportToolkit.Providers.Zammad.Deletion;

/// <summary>What to search for in Zammad; at least one term.</summary>
public sealed record ZammadSearchCriteria(
    string? Organization,
    string? RequesterEmail,
    string? EmailDomain,
    string? FreeText,
    string? SenderName,
    string? TicketNumber = null);

/// <summary>
/// Builds Zammad's ticket search query, as observed against Zammad 7.3.0
/// (lab/zammad/README.md): field terms joined with AND.
///
/// Zammad's search matches loosely (a phrase within a longer organization
/// name, a domain as an email suffix), so ZammadTicketSearchProvider then
/// keeps only exact matches. Quotes, backslashes and control characters
/// are refused rather than escaped.
/// </summary>
public static class ZammadSearchQuery
{
    public static string Build(ZammadSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var terms = new List<string>();

        if (Number(criteria.TicketNumber) is { } number)
        {
            // Observed: number:44013 matches that ticket only.
            terms.Add($"number:{number}");
        }

        if (Value(criteria.Organization) is { } organization)
        {
            terms.Add($"organization.name:{Quote(organization, "organization")}");
        }

        if (Value(criteria.RequesterEmail) is { } email)
        {
            if (email.IndexOf('@') <= 0 || email.EndsWith('@'))
            {
                throw new ArgumentException(
                    "The requester email is not an email address.");
            }

            terms.Add($"customer.email:{Quote(email, "requester email")}");
        }

        if (Domain(criteria.EmailDomain) is { } domain)
        {
            terms.Add($"customer.email:*{domain}");
        }

        if (Value(criteria.SenderName) is { } sender)
        {
            terms.Add($"customer.fullname:{Quote(sender, "sender name")}");
        }

        if (Value(criteria.FreeText) is { } text)
        {
            terms.Add(Quote(text, "search text"));
        }

        if (terms.Count == 0)
        {
            throw new ArgumentException(
                "Enter at least one search term: an empty search would " +
                "match every ticket.");
        }

        return string.Join(" AND ", terms);
    }

    /// <summary>
    /// The domain as searched: lower case, without a leading "@" or "*".
    /// Null when none was given.
    /// </summary>
    public static string? Domain(string? value)
    {
        var domain = Value(value)?.TrimStart('@', '*').ToLowerInvariant();

        if (domain is null)
        {
            return null;
        }

        if (domain.Length == 0
            || !domain.Contains('.')
            || domain.StartsWith('.')
            || domain.EndsWith('.')
            || !domain.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || character is '.' or '-'))
        {
            throw new ArgumentException(
                "The email domain must be a domain name, such as " +
                "example.com.");
        }

        return domain;
    }

    /// <summary>
    /// The ticket number as Zammad shows it (digits, optionally after
    /// "#" or "Ticket#"), or null when none was given.
    /// </summary>
    public static string? Number(string? value)
    {
        var number = Value(value);

        if (number is null)
        {
            return null;
        }

        if (number.StartsWith("Ticket", StringComparison.OrdinalIgnoreCase))
        {
            number = number[6..];
        }

        number = number.TrimStart('#').Trim();

        if (number.Length == 0 || !number.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                "A ticket number is the number Zammad shows, such as 44013.");
        }

        return number;
    }

    private static string? Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Quote(string value, string field)
    {
        if (value.Any(character =>
                character is '"' or '\\' || char.IsControl(character)))
        {
            throw new ArgumentException(
                $"The {field} cannot contain quotes, backslashes or " +
                "control characters.");
        }

        return "\"" + value + "\"";
    }
}
