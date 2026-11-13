
namespace SupportToolkit.Modules.ZendeskBulkDelete.Models;

public sealed record PlannedTicket(
    long Id,
    string Status,
    DeletionPreviewFlag? Flag,
    DateTimeOffset? UpdatedAt,
    bool LikelyArchived = false);

/// <summary>
/// The exact, frozen list of tickets a deletion will send.
///
/// It is built from a selection once, and then never changes. Deletion
/// sends only these IDs: the search is never run again at delete time, so
/// the set cannot drift between confirmation and deletion.
///
/// A plan is refused when the search was incomplete, when nothing is
/// selected, or when a selected ticket cannot be deleted.
/// </summary>
public sealed class DeletionPlan
{
    /// <summary>
    /// Zendesk accepts at most this many ticket IDs per deletion request.
    /// </summary>
    public const int BatchSize = 100;

    private DeletionPlan(
        Guid id,
        DateTimeOffset createdAt,
        string instance,
        DeletionOperator operatorUser,
        string query,
        IReadOnlyList<PlannedTicket> tickets)
    {
        Id = id;
        CreatedAt = createdAt;
        Instance = instance;
        Operator = operatorUser;
        Query = query;
        Tickets = tickets;
    }

    public Guid Id { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>The Zendesk subdomain the tickets belong to.</summary>
    public string Instance { get; }

    public DeletionOperator Operator { get; }

    public string Query { get; }

    /// <summary>The tickets to delete, in ID order.</summary>
    public IReadOnlyList<PlannedTicket> Tickets { get; }

    /// <summary>
    /// Tickets that are not closed (or whose closing time is unknown).
    /// Deleting them needs the explicit second confirmation.
    /// </summary>
    public IReadOnlyList<PlannedTicket> NonClosed =>
        Tickets.Where(ticket => ticket.Flag is not null).ToArray();

    /// <summary>
    /// Closed tickets old enough to be archived. Once deleted they are not
    /// in Zendesk's deleted-tickets view.
    /// </summary>
    public IReadOnlyList<PlannedTicket> LikelyArchived =>
        Tickets.Where(ticket => ticket.LikelyArchived).ToArray();

    /// <summary>The IDs in request-sized groups, in order.</summary>
    public IReadOnlyList<IReadOnlyList<long>> Batches =>
        Tickets
            .Select(ticket => ticket.Id)
            .Chunk(BatchSize)
            .Select(batch => (IReadOnlyList<long>)batch)
            .ToArray();

    public static DeletionPlan Create(
        DeletionPreviewResult preview,
        DeletionSelection selection,
        string instance,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);

        if (!preview.Preview.IsSearchComplete)
        {
            throw new InvalidOperationException(
                "The search is incomplete, so it is not the full set of " +
                "matching tickets. Narrow the search before deleting. " +
                preview.Preview.SearchIncompleteReason);
        }

        var selected = selection.SelectedTickets;

        if (selected.Count == 0)
        {
            throw new InvalidOperationException(
                "No tickets are selected.");
        }

        var blocked =
            selected.Where(
                    ticket => !DeletionSelection.CanBeSelected(ticket))
                .Select(ticket => "#" + ticket.Id)
                .ToArray();

        if (blocked.Length > 0)
        {
            throw new InvalidOperationException(
                "These tickets cannot be deleted yet (closed less than " +
                "24 hours ago): " + string.Join(", ", blocked) + ".");
        }

        return new DeletionPlan(
            Guid.NewGuid(),
            now,
            instance.Trim(),
            preview.Operator,
            preview.Query,
            selected
                .Select(
                    ticket => new PlannedTicket(
                        ticket.Id,
                        ticket.Status,
                        ticket.Flag,
                        ticket.UpdatedAt,
                        ticket.LikelyArchived))
                .OrderBy(ticket => ticket.Id)
                .ToArray());
    }
}
