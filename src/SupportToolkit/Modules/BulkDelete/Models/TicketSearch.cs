namespace SupportToolkit.Modules.BulkDelete.Models;

/// <summary>
/// What to search for. At least one field is required: an empty search
/// would select every ticket in the instance.
/// </summary>
public sealed record TicketSearchCriteria(
    string? Organization = null,
    string? RequesterEmail = null,
    string? EmailDomain = null,
    string? FreeText = null,
    string? SenderName = null)
{
    /// <summary>
    /// SenderName is the requester's name, which for an email ticket is
    /// the sender.
    /// </summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(Organization)
        || !string.IsNullOrWhiteSpace(RequesterEmail)
        || !string.IsNullOrWhiteSpace(EmailDomain)
        || !string.IsNullOrWhiteSpace(FreeText)
        || !string.IsNullOrWhiteSpace(SenderName);
}

/// <summary>
/// One ticket a search found. Id is what the system deletes by. Number is
/// what operators see in the system when it differs from the Id (Zammad's
/// ticket number); null when the Id is what they see (Zendesk).
/// </summary>
public sealed record TicketSummary(
    long Id,
    string? Status,
    string? Subject,
    long? RequesterId,
    long? OrganizationId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? Description = null,
    string? Number = null);

/// <summary>
/// The tickets a search returned, with an explicit completeness verdict.
/// An incomplete result must never be shown or acted on as if it were
/// the whole set.
/// </summary>
public sealed record TicketSearchResult(
    IReadOnlyList<TicketSummary> Tickets,
    int? ReportedCount,
    int DuplicateCount,
    bool IsComplete,
    string? IncompleteReason);

/// <summary>
/// The signed-in operator. Shown to the operator and recorded in the
/// receipt; never logged.
/// </summary>
public sealed record DeletionOperator(
    long Id,
    string? Name,
    string? Email,
    string? Role);
