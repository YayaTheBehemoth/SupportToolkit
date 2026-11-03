using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// The monitored review an operator is working with, and what may be done
/// with it. It holds the state that used to be a local variable in the
/// console menu, so any interface (the console, a web page) drives the
/// same rules.
///
/// States and rules:
///  - Nothing has been reviewed until RunReviewAsync succeeds. A refresh
///    that fails leaves no current review, as before.
///  - A ticket draft or submission plan is always built from the current
///    review, and a missing or incomplete review is a typed result.
///  - A submission plan is single-use and only valid for the review it
///    was prepared from: re-running the review, or submitting once,
///    invalidates it. Submitting a stale or foreign plan is refused, so
///    nothing the operator did not confirm is ever sent.
///
/// It contains no console or user-interface code and is not thread-safe:
/// one session belongs to one operator.
/// </summary>
public sealed class BackupReviewSession
{
    private readonly BackupAggregatorWorkflow _workflow;
    private readonly BackupAggregatorScopedWorkflow? _scopedWorkflow;
    private readonly TimeProvider _timeProvider;

    private TicketSubmissionPlan? _preparedPlan;

    public BackupReviewSession(
        BackupAggregatorWorkflow workflow,
        BackupAggregatorScopedWorkflow? scopedWorkflow,
        TimeProvider? timeProvider = null)
    {
        _workflow =
            workflow ?? throw new ArgumentNullException(nameof(workflow));

        _scopedWorkflow = scopedWorkflow;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// The review the operator has run, or null before one has.
    /// </summary>
    public ScopedBackupInventoryReviewResult? CurrentReview
    {
        get;
        private set;
    }

    public ReviewSummary? CurrentSummary =>
        CurrentReview is null
            ? null
            : ReviewSummary.From(CurrentReview);

    public async Task<ScopedReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_scopedWorkflow is null)
        {
            return new ScopedReviewAvailability(
                await _workflow.GetModeAsync(cancellationToken),
                false,
                "The monitored review is not available in this host.");
        }

        return await _scopedWorkflow.GetAvailabilityAsync(
            cancellationToken);
    }

    /// <summary>
    /// Runs (or refreshes) the monitored review for today. The previous
    /// review, and any plan prepared from it, is discarded first.
    /// </summary>
    public async Task<ScopedBackupInventoryReviewResult>
        RunReviewAsync(
            CancellationToken cancellationToken = default)
    {
        if (_scopedWorkflow is null)
        {
            throw new InvalidOperationException(
                "The monitored review is not available in this host.");
        }

        CurrentReview = null;
        _preparedPlan = null;

        var review =
            await _scopedWorkflow.ReviewAsync(
                DateOnly.FromDateTime(
                    _timeProvider.GetLocalNow().DateTime),
                cancellationToken);

        CurrentReview = review;

        return review;
    }

    /// <summary>
    /// The draft for the current review, for previewing. Needs no Zendesk
    /// configuration.
    /// </summary>
    public TicketPreparation<TicketDraft> PreviewTicket() =>
        _workflow.PrepareScopedTicketDraft(CurrentReview);

    /// <summary>
    /// Prepares the submission for the operator to confirm. A ready plan
    /// is remembered as the only one that may be submitted.
    /// </summary>
    public async Task<TicketPreparation<TicketSubmissionPlan>>
        PrepareSubmissionAsync(
            CancellationToken cancellationToken = default)
    {
        _preparedPlan = null;

        var preparation =
            await _workflow.PrepareScopedTicketSubmissionAsync(
                CurrentReview,
                cancellationToken);

        if (preparation.IsReady)
        {
            _preparedPlan = preparation.Value;
        }

        return preparation;
    }

    /// <summary>
    /// Submits the plan returned by the last PrepareSubmissionAsync, once.
    /// </summary>
    public async Task<CreatedTicket> SubmitAsync(
        TicketSubmissionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!ReferenceEquals(plan, _preparedPlan))
        {
            throw new InvalidOperationException(
                "This submission is no longer current: the review was " +
                "run again, or it was already submitted. Prepare and " +
                "confirm it again.");
        }

        // Single use, whatever the outcome: a retry is prepared and
        // confirmed again, and Zendesk's idempotency key protects a
        // genuine repeat.
        _preparedPlan = null;

        return await _workflow.SubmitPlannedTicketAsync(
            plan,
            cancellationToken);
    }
}
