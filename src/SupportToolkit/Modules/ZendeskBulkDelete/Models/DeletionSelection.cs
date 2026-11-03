namespace SupportToolkit.Modules.ZendeskBulkDelete.Models;

/// <summary>
/// Which of a preview's tickets are chosen for deletion.
///
/// It starts as the preview's default (closed tickets, old enough). Tickets
/// that are not closed can be added explicitly, which later needs the extra
/// confirmation. A ticket closed less than 24 hours ago cannot be deleted
/// through the API, so it can never be selected.
///
/// Changes are all-or-nothing: if any ID is unknown or not selectable,
/// nothing changes. It holds only ticket IDs and what the preview already
/// said about them; it has no connection to Zendesk and no UI.
/// </summary>
public sealed class DeletionSelection
{
    private readonly Dictionary<long, DeletionPreviewTicket> _tickets;
    private readonly HashSet<long> _selected;

    public DeletionSelection(
        DeletionPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        _tickets =
            preview.Tickets.ToDictionary(ticket => ticket.Id);

        _selected =
            preview.Tickets
                .Where(ticket => ticket.Selected)
                .Select(ticket => ticket.Id)
                .ToHashSet();
    }

    public int Count => _selected.Count;

    public IReadOnlyList<DeletionPreviewTicket> SelectedTickets =>
        _selected
            .Order()
            .Select(id => _tickets[id])
            .ToArray();

    /// <summary>
    /// Selected tickets that are not closed, or whose closing time is
    /// unknown. They need the explicit second confirmation.
    /// </summary>
    public IReadOnlyList<DeletionPreviewTicket> SelectedNonClosed =>
        SelectedTickets
            .Where(ticket => ticket.Flag is not null)
            .ToArray();

    public bool IsSelected(long ticketId) =>
        _selected.Contains(ticketId);

    /// <summary>
    /// False for a ticket closed less than 24 hours ago.
    /// </summary>
    public static bool CanBeSelected(
        DeletionPreviewTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return ticket.Flag != DeletionPreviewFlag.ClosedWithin24Hours;
    }

    public void Select(
        IEnumerable<long> ticketIds)
    {
        var ids = Resolve(ticketIds);

        var blocked =
            ids.Where(id => !CanBeSelected(_tickets[id]))
                .Order()
                .ToArray();

        if (blocked.Length > 0)
        {
            throw new ArgumentException(
                "Closed less than 24 hours ago, so Zendesk cannot " +
                "delete: " + string.Join(", ", blocked.Select(Show)) + ".");
        }

        _selected.UnionWith(ids);
    }

    public void Deselect(
        IEnumerable<long> ticketIds)
    {
        _selected.ExceptWith(Resolve(ticketIds));
    }

    /// <summary>Back to the preview's default selection.</summary>
    public void Reset()
    {
        _selected.Clear();

        _selected.UnionWith(
            _tickets.Values
                .Where(ticket => ticket.Selected)
                .Select(ticket => ticket.Id));
    }

    public void Clear()
    {
        _selected.Clear();
    }

    private HashSet<long> Resolve(
        IEnumerable<long> ticketIds)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        var ids = ticketIds.ToHashSet();

        var unknown =
            ids.Where(id => !_tickets.ContainsKey(id))
                .Order()
                .ToArray();

        if (unknown.Length > 0)
        {
            throw new ArgumentException(
                "Not in this search result: " +
                string.Join(", ", unknown.Select(Show)) + ".");
        }

        return ids;
    }

    private static string Show(long id) => "#" + id;
}
