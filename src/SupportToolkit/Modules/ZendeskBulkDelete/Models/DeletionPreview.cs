using SupportToolkit.Providers.Zendesk.Deletion.Search;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Models;

/// <summary>
/// Why a ticket is not selected for deletion by default.
/// </summary>
public enum DeletionPreviewFlag
{
    /// <summary>The ticket is not closed.</summary>
    NotClosed,

    /// <summary>
    /// Closed less than 24 hours ago, which the API cannot delete.
    /// </summary>
    ClosedWithin24Hours,

    /// <summary>
    /// Closed, but when is not known, so it is treated as too recent.
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
    DeletionPreviewFlag? Flag);

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
    string? DirectoryNote);

/// <summary>
/// The signed-in operator and the preview of their search.
/// </summary>
public sealed record DeletionPreviewResult(
    ZendeskOperator Operator,
    string Query,
    DeletionPreview Preview);
