
namespace SupportToolkit.Modules.BulkDelete.Models;

/// <summary>
/// Why a ticket is not selected for deletion by default.
/// </summary>
public enum DeletionPreviewFlag
{
    /// <summary>The ticket is not closed.</summary>
    NotClosed,

    /// <summary>
    /// Closed more recently than the system allows deleting
    /// (TicketSystemRules.DeleteBlockedAfterClosing; 24 hours in Zendesk).
    /// </summary>
    ClosedWithin24Hours,

    /// <summary>
    /// Closed, but when is not known, so it is treated as too recent. Only
    /// for a system with a waiting time after closing.
    /// </summary>
    ClosedTimeUnknown
}

public sealed record DeletionPreviewTicket(
    long Id,
    string Status,
    string? Subject,
    string Organization,
    string RequesterDomain,
    DateTimeOffset? UpdatedAt,
    bool Selected,
    DeletionPreviewFlag? Flag,
    bool LikelyArchived = false,
    string? Description = null,
    string? Number = null)
{
    /// <summary>
    /// The ticket as operators know it: "#" and its number, or its ID when
    /// the system shows the ID.
    /// </summary>
    public string Shown => TicketDisplay.Show(Id, Number);
}

/// <summary>How a ticket is named to operators.</summary>
public static class TicketDisplay
{
    public static string Show(long id, string? number) =>
        "#" + (string.IsNullOrWhiteSpace(number)
            ? id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : number.Trim());
}

public sealed record PreviewGroup(
    string Name,
    int Count);

/// <summary>
/// An exception-first view of what a search found, before any decision to
/// delete. It contains no deletion capability.
///
/// Closed tickets are selected by default. Tickets that are not closed, or
/// were closed too recently, are listed as exceptions and are not selected.
/// An incomplete search or incomplete name lookup is stated, never hidden.
/// </summary>
public sealed record DeletionPreview(
    int Total,
    int SelectedCount,
    IReadOnlyList<PreviewGroup> StatusCounts,
    IReadOnlyList<PreviewGroup> ByOrganization,
    IReadOnlyList<PreviewGroup> ByRequesterDomain,
    IReadOnlyList<DeletionPreviewTicket> Exceptions,
    IReadOnlyList<DeletionPreviewTicket> Tickets,
    bool IsSearchComplete,
    string? SearchIncompleteReason,
    int DuplicateCount,
    string? DirectoryNote)
{
    /// <summary>
    /// The rules of the system searched, which the selection and the plan
    /// made from this preview follow.
    /// </summary>
    public required TicketSystemRules Rules { get; init; }
}

/// <summary>
/// The signed-in operator and the preview of their search.
/// </summary>
public sealed record DeletionPreviewResult(
    DeletionOperator Operator,
    string Query,
    DeletionPreview Preview);
