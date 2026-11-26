namespace SupportToolkit.Providers.Zendesk.Deletion.Search;

/// <summary>
/// What to search for. At least one field is required: an empty search
/// would select every ticket in the instance.
/// </summary>
public sealed record ZendeskSearchCriteria(
    string? Organization = null,
    string? RequesterEmail = null,
    string? EmailDomain = null,
    string? FreeText = null,
    string? SenderName = null,
    long? TicketId = null)
{
    /// <summary>
    /// SenderName is the requester's name, which for an email ticket is
    /// the sender. Zendesk's requester operator takes a full or partial
    /// name as well as an email address. TicketId is searched for as a
    /// term, and only the ticket with exactly that ID is kept.
    /// </summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Organization)
        || !string.IsNullOrWhiteSpace(RequesterEmail)
        || !string.IsNullOrWhiteSpace(EmailDomain)
        || !string.IsNullOrWhiteSpace(FreeText)
        || !string.IsNullOrWhiteSpace(SenderName)
        || TicketId is not null;
}

/// <summary>
/// Builds the Zendesk search query string. Always restricted to tickets.
/// </summary>
public static class ZendeskSearchQuery
{
    private const int MaxFreeTextLength = 500;

    public static string Build(
        ZendeskSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (!criteria.HasAny)
        {
            throw new ArgumentException(
                "At least one search criterion is required. An empty " +
                "search would select every ticket.",
                nameof(criteria));
        }

        var terms = new List<string> { "type:ticket" };

        if (!string.IsNullOrWhiteSpace(criteria.Organization))
        {
            terms.Add(
                "organization:" + Quote(criteria.Organization));
        }

        if (!string.IsNullOrWhiteSpace(criteria.RequesterEmail))
        {
            var email = criteria.RequesterEmail.Trim();

            if (!email.Contains('@')
                || email.Any(char.IsWhiteSpace)
                || email.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "The requester must be an email address.",
                    nameof(criteria));
            }

            // Zendesk documents requester:address unquoted, and says
            // quoting an email does not give an exact match. The address
            // has no whitespace here, so no quoting is needed.
            terms.Add("requester:" + email);
        }

        if (!string.IsNullOrWhiteSpace(criteria.SenderName))
        {
            terms.Add("requester:" + Quote(criteria.SenderName));
        }

        if (!string.IsNullOrWhiteSpace(criteria.EmailDomain))
        {
            terms.Add(
                "requester:*@" + NormalizeDomain(criteria.EmailDomain));
        }

        if (criteria.TicketId is { } ticketId)
        {
            if (ticketId <= 0)
            {
                throw new ArgumentException(
                    "A ticket number is a positive whole number.",
                    nameof(criteria));
            }

            // Unverified against Zendesk: a bare number is searched as
            // text, and the result is narrowed to that ID afterwards, so
            // a search that does not return it finds nothing.
            terms.Add(ticketId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(criteria.FreeText))
        {
            var text =
                new string(
                    criteria.FreeText
                        .Where(character => !char.IsControl(character))
                        .ToArray())
                    .Trim();

            if (text.Length > MaxFreeTextLength)
            {
                throw new ArgumentException(
                    "The search text is too long.",
                    nameof(criteria));
            }

            if (text.Length > 0)
            {
                terms.Add(text);
            }
        }

        return string.Join(" ", terms);
    }

    /// <summary>
    /// A domain such as example.com, with an optional leading "@".
    /// </summary>
    public static string NormalizeDomain(
        string domain)
    {
        var value =
            domain.Trim().TrimStart('@').ToLowerInvariant();

        if (value.Length == 0
            || value.StartsWith('.')
            || value.EndsWith('.')
            || !value.All(
                character =>
                    character is >= 'a' and <= 'z'
                        or >= '0' and <= '9'
                        or '.'
                        or '-'))
        {
            throw new ArgumentException(
                "The email domain may contain only letters, numbers, " +
                "dots and hyphens.",
                nameof(domain));
        }

        return value;
    }

    private static string Quote(
        string value)
    {
        var text = value.Trim();

        if (text.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Search values cannot contain control characters.");
        }

        return "\"" +
               text.Replace("\\", "\\\\").Replace("\"", "\\\"") +
               "\"";
    }
}
