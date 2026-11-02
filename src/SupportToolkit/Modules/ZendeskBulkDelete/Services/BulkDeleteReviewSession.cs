using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Providers.Zendesk.Deletion.Search;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

public enum BulkDeleteStage
{
    SignedOut,
    SignedIn,

    /// <summary>A search has been run and a selection exists.</summary>
    Previewed,

    /// <summary>The selection is frozen into a plan, awaiting confirmation.</summary>
    Planned,

    Deleting,

    /// <summary>A deletion ended: completed, stopped with problems or cancelled.</summary>
    Finished
}

/// <summary>What one dry-run request would be.</summary>
public sealed record DryRunBatch(
    int Number,
    int TicketCount,
    long FirstId,
    long LastId);

public sealed record PlanAttempt(
    DeletionPlan? Plan,
    string? Problem);

/// <summary>
/// What an operator must type before a deletion may start.
/// </summary>
public sealed record DeletionRequirements(
    string Instance,
    int TicketCount,
    IReadOnlyList<PlannedTicket> NonClosed)
{
    public bool NeedsNonClosedConfirmation => NonClosed.Count > 0;
}

/// <summary>
/// The result of checking what the operator typed against the plan. The
/// executor checks again; this lets an interface enable its button.
/// </summary>
public sealed record ConfirmationCheck(
    bool CountMatches,
    bool NonClosedMatches,
    DeletionConfirmation? Confirmation)
{
    public bool IsReady => Confirmation is not null;
}

public sealed record DeletionProblemGroup(
    TicketDeletionState State,
    string? Detail,
    IReadOnlyList<long> TicketIds);

public sealed record BulkDeleteResultSummary(
    int Deleted,
    int Failed,
    int Unknown,
    int NotAttempted,
    IReadOnlyList<DeletionProblemGroup> Problems,
    string ReceiptPath,
    string? ReceiptWarning)
{
    /// <summary>Every ticket reported deleted and the receipt complete.</summary>
    public bool IsClean =>
        Failed == 0
        && Unknown == 0
        && NotAttempted == 0
        && ReceiptWarning is null;

    public static BulkDeleteResultSummary From(DeletionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var outcome = run.Outcome;

        return new BulkDeleteResultSummary(
            outcome.Count(TicketDeletionState.Deleted),
            outcome.Count(TicketDeletionState.Failed),
            outcome.Count(TicketDeletionState.Unknown),
            outcome.Count(TicketDeletionState.NotAttempted),
            outcome.Results
                .Where(
                    result => result.State
                        is TicketDeletionState.Failed
                        or TicketDeletionState.Unknown)
                .GroupBy(result => (result.State, result.Detail))
                .OrderBy(group => group.Key.State)
                .Select(
                    group => new DeletionProblemGroup(
                        group.Key.State,
                        group.Key.Detail,
                        group.Select(result => result.Id).Order().ToArray()))
                .ToArray(),
            run.ReceiptPath,
            run.ReceiptWarning);
    }
}

/// <summary>
/// One operator's bulk-delete work, from sign-in to receipt, as a state
/// machine any interface can drive. It holds the state that the console
/// flow keeps in local variables, and no console or web code.
///
/// Rules:
///  - A search needs a sign-in, and replaces the previous search, selection
///    and plan.
///  - Any change to the selection discards the plan; a plan is only valid
///    for the selection it was made from.
///  - A plan is refused for an incomplete search, an empty selection or a
///    ticket that cannot be deleted yet (DeletionPlan.Create).
///  - Deleting is single-use: once started, the plan cannot be run again,
///    and the selection cannot change. The executor still checks the typed
///    confirmations itself.
///  - Deletion happens only if the session was given an executor.
///
/// Not thread-safe for concurrent callers except that Stage, Progress and
/// the results may be read while ExecuteAsync runs.
/// </summary>
public sealed class BulkDeleteReviewSession
{
    private readonly Func<CancellationToken, Task<ZendeskOperator>> _signIn;

    private readonly Func<ZendeskOperator, ZendeskSearchCriteria,
        CancellationToken, Task<DeletionPreviewResult>> _preview;

    private readonly ZendeskDeletionExecutor? _executor;
    private readonly TimeProvider _timeProvider;

    private readonly object _lock = new();

    private DeletionSelection? _selection;

    public BulkDeleteReviewSession(
        string instance,
        Func<CancellationToken, Task<ZendeskOperator>> signIn,
        Func<ZendeskOperator, ZendeskSearchCriteria, CancellationToken,
            Task<DeletionPreviewResult>> preview,
        ZendeskDeletionExecutor? executor,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instance);

        Instance = instance.Trim();
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _executor = executor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Builds a session over the real connection.</summary>
    public static BulkDeleteReviewSession From(
        ZendeskBulkDeleteSession session,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new BulkDeleteReviewSession(
            session.Options.Subdomain,
            session.Workflow.SignInAsync,
            session.Workflow.PreviewAsync,
            session.Executor,
            timeProvider);
    }

    /// <summary>The Zendesk subdomain tickets are deleted from.</summary>
    public string Instance { get; }

    public bool CanDelete => _executor is not null;

    public BulkDeleteStage Stage { get; private set; } =
        BulkDeleteStage.SignedOut;

    public ZendeskOperator? Operator { get; private set; }

    public DeletionPreviewResult? Preview { get; private set; }

    /// <summary>The search terms the current preview came from.</summary>
    public ZendeskSearchCriteria? Criteria { get; private set; }

    public DeletionPlan? Plan { get; private set; }

    /// <summary>The latest progress event of a running deletion.</summary>
    public DeletionProgress? LatestProgress { get; private set; }

    public BulkDeleteResultSummary? Result { get; private set; }

    /// <summary>
    /// Set when a deletion was cancelled. Says where the receipt is; the
    /// outcome of the tickets in flight is unknown.
    /// </summary>
    public string? CancelledMessage { get; private set; }

    public int SelectedCount => _selection?.Count ?? 0;

    public bool IsSelected(long ticketId) =>
        _selection?.IsSelected(ticketId) ?? false;

    // ---- Sign-in and search ----

    public async Task SignInAsync(
        CancellationToken cancellationToken = default)
    {
        RequireNot(BulkDeleteStage.Deleting);

        Operator = await _signIn(cancellationToken);

        Discard();
        Stage = BulkDeleteStage.SignedIn;
    }

    /// <summary>
    /// Runs the search. Returns a message when the search itself is not
    /// acceptable (nothing to search for, or an invalid term); a failed
    /// search throws, and leaves no preview.
    /// </summary>
    public async Task<string?> SearchAsync(
        ZendeskSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (Operator is null || Stage == BulkDeleteStage.SignedOut)
        {
            throw new InvalidOperationException("Sign in first.");
        }

        RequireNot(BulkDeleteStage.Deleting);

        if (!criteria.HasAny)
        {
            return "Enter at least one search term: an empty search would " +
                   "match every ticket.";
        }

        try
        {
            ZendeskSearchQuery.Build(criteria);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        Discard();
        Stage = BulkDeleteStage.SignedIn;

        var preview =
            await _preview(Operator, criteria, cancellationToken);

        Preview = preview;
        Criteria = criteria;
        _selection = new DeletionSelection(preview.Preview);
        Stage = BulkDeleteStage.Previewed;

        return null;
    }

    // ---- Selection ----

    public string? Select(IEnumerable<long> ticketIds) =>
        ChangeSelection(selection => selection.Select(ticketIds));

    public string? Deselect(IEnumerable<long> ticketIds) =>
        ChangeSelection(selection => selection.Deselect(ticketIds));

    /// <summary>
    /// Selects every ticket that can be deleted, including ones that are
    /// not closed (they still need the second confirmation). Tickets closed
    /// less than 24 hours ago are left out, since they cannot be deleted yet.
    /// </summary>
    public string? SelectAll() =>
        ChangeSelection(
            selection => selection.Select(
                Preview!.Preview.Tickets
                    .Where(DeletionSelection.CanBeSelected)
                    .Select(ticket => ticket.Id)));

    public string? ResetSelection() =>
        ChangeSelection(selection => selection.Reset());

    public string? ClearSelection() =>
        ChangeSelection(selection => selection.Clear());

    private string? ChangeSelection(Action<DeletionSelection> change)
    {
        if (_selection is null
            || Stage is not (BulkDeleteStage.Previewed
                or BulkDeleteStage.Planned))
        {
            throw new InvalidOperationException(
                "There is no selection that can be changed.");
        }

        try
        {
            change(_selection);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        // A plan is only valid for the selection it was made from.
        Plan = null;
        Stage = BulkDeleteStage.Previewed;

        return null;
    }

    // ---- Plan, dry run, confirmation ----

    public PlanAttempt PreparePlan()
    {
        if (Preview is null
            || _selection is null
            || Stage is not (BulkDeleteStage.Previewed
                or BulkDeleteStage.Planned))
        {
            throw new InvalidOperationException("Search first.");
        }

        Plan = null;
        Stage = BulkDeleteStage.Previewed;

        if (_selection.Count == 0)
        {
            return new PlanAttempt(
                null,
                "Nothing is selected, so there is nothing to delete.");
        }

        try
        {
            Plan =
                DeletionPlan.Create(
                    Preview,
                    _selection,
                    Instance,
                    _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return new PlanAttempt(null, exception.Message);
        }

        Stage = BulkDeleteStage.Planned;

        return new PlanAttempt(Plan, null);
    }

    /// <summary>
    /// Goes back from a plan to choosing tickets, keeping the selection.
    /// </summary>
    public void DiscardPlan()
    {
        if (Stage != BulkDeleteStage.Planned)
        {
            return;
        }

        Plan = null;
        Stage = BulkDeleteStage.Previewed;
    }

    /// <summary>
    /// The requests a deletion would send. Sends nothing.
    /// </summary>
    public IReadOnlyList<DryRunBatch> DryRun()
    {
        var plan = RequirePlan();

        return plan.Batches
            .Select(
                (batch, index) => new DryRunBatch(
                    index + 1,
                    batch.Count,
                    batch[0],
                    batch[^1]))
            .ToArray();
    }

    public DeletionRequirements Requirements()
    {
        var plan = RequirePlan();

        return new DeletionRequirements(
            plan.Instance,
            plan.Tickets.Count,
            plan.NonClosed);
    }

    /// <summary>
    /// Compares what the operator typed with the plan. Anything that is
    /// not exactly the right whole number does not match.
    /// </summary>
    public ConfirmationCheck CheckConfirmation(
        string? typedCount,
        string? typedNonClosedCount)
    {
        var requirements = Requirements();

        var count =
            int.TryParse(typedCount?.Trim(), out var typed)
            && typed == requirements.TicketCount;

        var nonClosedNeeded = requirements.NeedsNonClosedConfirmation;

        var nonClosed =
            !nonClosedNeeded
            || (int.TryParse(typedNonClosedCount?.Trim(), out var typedNon)
                && typedNon == requirements.NonClosed.Count);

        return new ConfirmationCheck(
            count,
            nonClosed,
            count && nonClosed
                ? new DeletionConfirmation(
                    requirements.TicketCount,
                    nonClosedNeeded ? requirements.NonClosed.Count : null)
                : null);
    }

    // ---- Deletion ----

    /// <summary>
    /// Deletes the plan's tickets. Single use. A cancellation ends in
    /// Finished with CancelledMessage set; any other failure throws and
    /// the stage is Finished too, since the outcome may be partial. A
    /// confirmation that does not match the plan is refused before
    /// anything starts and leaves the plan usable.
    /// </summary>
    public async Task ExecuteAsync(
        DeletionConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        DeletionPlan plan;

        lock (_lock)
        {
            if (_executor is null)
            {
                throw new InvalidOperationException(
                    "Deletion is not enabled for this session.");
            }

            if (Stage != BulkDeleteStage.Planned || Plan is null)
            {
                throw new InvalidOperationException(
                    "There is no confirmed plan to run. It may already " +
                    "have been used.");
            }

            // Checked here, before anything starts, so a wrong number
            // leaves the plan unspent. The executor checks again.
            if (confirmation.ConfirmedCount != Plan.Tickets.Count
                || (Plan.NonClosed.Count > 0
                    && confirmation.ConfirmedNonClosedCount
                        != Plan.NonClosed.Count)
                || (Plan.NonClosed.Count == 0
                    && confirmation.ConfirmedNonClosedCount is not (null or 0)))
            {
                throw new InvalidOperationException(
                    "The confirmed numbers do not match the plan. " +
                    "Nothing was deleted.");
            }

            plan = Plan;
            Stage = BulkDeleteStage.Deleting;
            LatestProgress = null;
            Result = null;
            CancelledMessage = null;
        }

        try
        {
            var run =
                await _executor.ExecuteAsync(
                    plan,
                    confirmation,
                    progress => LatestProgress = progress,
                    cancellationToken);

            Result = BulkDeleteResultSummary.From(run);
        }
        catch (OperationCanceledException exception)
        {
            CancelledMessage = exception.Message;
        }
        finally
        {
            lock (_lock)
            {
                if (Stage == BulkDeleteStage.Deleting)
                {
                    Stage = BulkDeleteStage.Finished;
                }
            }
        }
    }

    /// <summary>
    /// Back to a signed-in session with no search, for the next job.
    /// Not possible while deleting.
    /// </summary>
    public void StartOver()
    {
        RequireNot(BulkDeleteStage.Deleting);

        if (Operator is null)
        {
            return;
        }

        Discard();
        Stage = BulkDeleteStage.SignedIn;
    }

    private void Discard()
    {
        Preview = null;
        Criteria = null;
        _selection = null;
        Plan = null;
        LatestProgress = null;
        Result = null;
        CancelledMessage = null;
    }

    private DeletionPlan RequirePlan() =>
        Plan ?? throw new InvalidOperationException(
            "There is no plan. Prepare one first.");

    private void RequireNot(BulkDeleteStage stage)
    {
        if (Stage == stage)
        {
            throw new InvalidOperationException(
                "This is not possible while a deletion is running.");
        }
    }
}
