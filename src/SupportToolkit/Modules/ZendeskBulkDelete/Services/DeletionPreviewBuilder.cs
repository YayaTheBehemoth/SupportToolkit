using SupportToolkit.Modules.ZendeskBulkDelete.Models;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Builds the exception-first deletion preview. Pure: no I/O.
/// </summary>
public static class DeletionPreviewBuilder
{
    public const string NoOrganization = "(no organization)";
    public const string NoRequester = "(no requester)";
    public const string Unknown = "(unknown)";

    /// <summary>
    /// Zendesk cannot delete a ticket closed less than this long ago.
    /// </summary>
    public static readonly TimeSpan RecentlyClosedWindow =
        TimeSpan.FromHours(24);

    /// <summary>
    /// Zendesk archives closed tickets after about this long. A deleted
    /// archived ticket is not in the 30-day deleted-tickets view and can
    /// only be restored through the API by ID.
    /// </summary>
    public static readonly TimeSpan ArchiveAge =
        TimeSpan.FromDays(120);

    /// <summary>
    /// True for a closed ticket last updated at least <see cref="ArchiveAge"/>
    /// ago. An approximation: the real close time is not in the search
    /// result, and an unknown time is not claimed to be archived.
    /// </summary>
    public static bool IsLikelyArchived(
        string? status,
        DateTimeOffset? updatedAt,
        DateTimeOffset now) =>
        string.Equals(status?.Trim(), "closed", StringComparison.OrdinalIgnoreCase)
        && updatedAt is not null
        && now - updatedAt.Value >= ArchiveAge;

    /// <param name="organizationNames">
    /// Null when the lookup failed: every organization is then unknown.
    /// </param>
    /// <param name="userEmails">
    /// Null when the lookup failed: every requester domain is then unknown.
    /// </param>
    public static DeletionPreview Build(
        TicketSearchResult search,
        IReadOnlyDictionary<long, string>? organizationNames,
        IReadOnlyDictionary<long, string>? userEmails,
        string? directoryNote,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(search);

        var tickets =
            search.Tickets
                .Select(
                    ticket => ToPreviewTicket(
                        ticket,
                        organizationNames,
                        userEmails,
                        now))
                .OrderBy(ticket => ticket.Id)
                .ToArray();

        return new DeletionPreview(
            tickets.Length,
            tickets.Count(ticket => ticket.Selected),
            Group(tickets.Select(ticket => ticket.Status)),
            Group(tickets.Select(ticket => ticket.Organization)),
            Group(tickets.Select(ticket => ticket.RequesterDomain)),
            tickets.Where(ticket => ticket.Flag is not null).ToArray(),
            tickets,
            search.IsComplete,
            search.IncompleteReason,
            search.DuplicateCount,
            directoryNote);
    }

    private static DeletionPreviewTicket ToPreviewTicket(
        TicketSummary ticket,
        IReadOnlyDictionary<long, string>? organizationNames,
        IReadOnlyDictionary<long, string>? userEmails,
        DateTimeOffset now)
    {
        var status =
            string.IsNullOrWhiteSpace(ticket.Status)
                ? Unknown
                : ticket.Status.Trim().ToLowerInvariant();

        DeletionPreviewFlag? flag = null;

        if (status != "closed")
        {
            flag = DeletionPreviewFlag.NotClosed;
        }
        else if (ticket.UpdatedAt is null)
        {
            // Without a time, "closed long enough ago" cannot be known.
            flag = DeletionPreviewFlag.ClosedTimeUnknown;
        }
        else if (now - ticket.UpdatedAt.Value < RecentlyClosedWindow)
        {
            flag = DeletionPreviewFlag.ClosedWithin24Hours;
        }

        return new DeletionPreviewTicket(
            ticket.Id,
            status,
            ticket.Subject,
            Organization(ticket.OrganizationId, organizationNames),
            RequesterDomain(ticket.RequesterId, userEmails),
            ticket.UpdatedAt,
            flag is null,
            flag,
            IsLikelyArchived(status, ticket.UpdatedAt, now),
            ticket.Description);
    }

    private static string Organization(
        long? organizationId,
        IReadOnlyDictionary<long, string>? names)
    {
        if (organizationId is null or <= 0)
        {
            return NoOrganization;
        }

        return names is not null
               && names.TryGetValue(organizationId.Value, out var name)
            ? name
            : Unknown;
    }

    private static string RequesterDomain(
        long? requesterId,
        IReadOnlyDictionary<long, string>? emails)
    {
        if (requesterId is null or <= 0)
        {
            return NoRequester;
        }

        if (emails is null
            || !emails.TryGetValue(requesterId.Value, out var email))
        {
            return Unknown;
        }

        var at = email.LastIndexOf('@');

        return at > 0 && at < email.Length - 1
            ? email[(at + 1)..].Trim().ToLowerInvariant()
            : Unknown;
    }

    private static IReadOnlyList<PreviewGroup> Group(
        IEnumerable<string> values) =>
        values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PreviewGroup(group.Key, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
