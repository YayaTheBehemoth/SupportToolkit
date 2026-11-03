namespace SupportToolkit.Modules.ZendeskBulkDelete.Models;

/// <summary>
/// What is known about one ticket after a deletion.
///
/// Unknown means the request or its job ended in a way that does not say
/// whether the ticket was deleted. It is never reported as deleted, and
/// the operator checks Zendesk's deleted tickets view.
/// </summary>
public enum TicketDeletionState
{
    /// <summary>Zendesk reported the ticket deleted.</summary>
    Deleted,

    /// <summary>Zendesk reported that it did not delete the ticket.</summary>
    Failed,

    /// <summary>It is not known whether the ticket was deleted.</summary>
    Unknown,

    /// <summary>No request was sent for the ticket.</summary>
    NotAttempted
}

public sealed record TicketDeletionResult(
    long Id,
    TicketDeletionState State,
    string? Detail,
    string? JobId);

/// <summary>
/// The two typed confirmations a deletion needs, as numbers: the count of
/// tickets to delete, and, when any of them are not closed, the count of
/// those. A UI collects them however suits it; they must match the plan.
/// </summary>
public sealed record DeletionConfirmation(
    int ConfirmedCount,
    int? ConfirmedNonClosedCount);

public enum DeletionProgressPhase
{
    StartingBatch,
    WaitingForJob,
    BatchFinished,
    Stopped
}

/// <summary>
/// A progress event for a UI. Carries no ticket content.
/// </summary>
public sealed record DeletionProgress(
    DeletionProgressPhase Phase,
    int Batch,
    int BatchCount,
    int TicketsInBatch,
    string? JobStatus,
    int? JobProgress,
    int? JobTotal,
    string? Message);

public sealed record DeletionOutcome(
    DeletionPlan Plan,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<TicketDeletionResult> Results)
{
    public int Count(TicketDeletionState state) =>
        Results.Count(result => result.State == state);

    /// <summary>
    /// True only when every planned ticket was reported deleted.
    /// </summary>
    public bool IsComplete =>
        Results.Count == Plan.Tickets.Count
        && Results.All(result => result.State == TicketDeletionState.Deleted);
}

/// <summary>
/// A finished deletion run, with where its receipt was written.
/// ReceiptWarning is set if the final receipt could not be written; the
/// receipt written before deletion started still lists every ticket.
/// </summary>
public sealed record DeletionRun(
    DeletionOutcome Outcome,
    string ReceiptPath,
    string? ReceiptWarning);
