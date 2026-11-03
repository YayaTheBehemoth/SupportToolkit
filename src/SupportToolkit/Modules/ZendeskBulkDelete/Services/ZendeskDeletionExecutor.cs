using System.Net;
using System.Text.Json;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Deleting;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Deletes the tickets of a confirmed plan. No console or UI code.
///
/// Rules it keeps:
///  - It deletes only the plan's frozen IDs and never searches again.
///  - It refuses to start unless both typed confirmations match the plan.
///  - A receipt listing every ticket is written BEFORE the first request;
///    if that cannot be written, nothing is deleted.
///  - Batches of up to 100 go one at a time, and each job is followed to
///    its end. There are no automatic retries.
///  - A ticket is Deleted only when Zendesk's job reports it. Anything
///    unclear is Unknown, never Deleted.
///  - After a request is rejected, a job fails or times out, or an outcome
///    is unclear, it stops: the remaining tickets are NotAttempted.
///  - A final receipt is written even when the run stops or is cancelled.
/// </summary>
public sealed class ZendeskDeletionExecutor
{
    public static readonly TimeSpan DefaultPollInterval =
        TimeSpan.FromSeconds(2);

    public static readonly TimeSpan DefaultMaxJobWait =
        TimeSpan.FromMinutes(5);

    private readonly ZendeskTicketDeletionProvider _provider;
    private readonly DeletionReceiptStore _receipts;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxPolls;

    public ZendeskDeletionExecutor(
        ZendeskTicketDeletionProvider provider,
        DeletionReceiptStore receipts,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? pollInterval = null,
        TimeSpan? maxJobWait = null)
    {
        _provider =
            provider ?? throw new ArgumentNullException(nameof(provider));

        _receipts =
            receipts ?? throw new ArgumentNullException(nameof(receipts));

        _timeProvider = timeProvider ?? TimeProvider.System;

        _delay =
            delay
            ?? ((interval, token) =>
                Task.Delay(interval, _timeProvider, token));

        _pollInterval = pollInterval ?? DefaultPollInterval;

        var wait = maxJobWait ?? DefaultMaxJobWait;

        // Counted in polls, not read from a clock, so it does not
        // depend on real time passing.
        _maxPolls =
            _pollInterval <= TimeSpan.Zero
                ? 1
                : Math.Max(
                    1,
                    (int)Math.Ceiling(wait / _pollInterval));
    }

    public async Task<DeletionRun> ExecuteAsync(
        DeletionPlan plan,
        DeletionConfirmation confirmation,
        Action<DeletionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);

        RequireConfirmation(plan, confirmation);

        var startedAt = _timeProvider.GetUtcNow();
        var receiptPath = _receipts.PathFor(plan.Id, startedAt);

        var results =
            plan.Tickets.ToDictionary(
                ticket => ticket.Id,
                ticket => new TicketDeletionResult(
                    ticket.Id,
                    TicketDeletionState.NotAttempted,
                    null,
                    null));

        // Written before any request. If it cannot be written, this
        // throws and nothing has been deleted.
        await _receipts.WriteAsync(
            receiptPath,
            DeletionReceipt.From(
                plan,
                confirmation,
                startedAt,
                null,
                results.Values),
            cancellationToken);

        var batches = plan.Batches;
        var cancelled = false;

        for (var index = 0; index < batches.Count; index++)
        {
            var batch = batches[index];
            var number = index + 1;

            progress?.Invoke(
                new DeletionProgress(
                    DeletionProgressPhase.StartingBatch,
                    number,
                    batches.Count,
                    batch.Count,
                    null,
                    null,
                    null,
                    null));

            bool stop;

            try
            {
                stop =
                    await RunBatchAsync(
                        batch,
                        number,
                        batches.Count,
                        results,
                        progress,
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // A job may already be running on Zendesk's side.
                MarkUnstarted(
                    batch,
                    results,
                    TicketDeletionState.Unknown,
                    "Cancelled while the deletion was in progress.",
                    null);

                cancelled = true;
                stop = true;
            }

            progress?.Invoke(
                new DeletionProgress(
                    stop
                        ? DeletionProgressPhase.Stopped
                        : DeletionProgressPhase.BatchFinished,
                    number,
                    batches.Count,
                    batch.Count,
                    null,
                    null,
                    null,
                    null));

            if (stop)
            {
                break;
            }
        }

        var finishedAt = _timeProvider.GetUtcNow();

        var outcome =
            new DeletionOutcome(
                plan,
                startedAt,
                finishedAt,
                plan.Tickets
                    .Select(ticket => results[ticket.Id])
                    .ToArray());

        string? warning = null;

        try
        {
            await _receipts.WriteAsync(
                receiptPath,
                DeletionReceipt.From(
                    plan,
                    confirmation,
                    startedAt,
                    finishedAt,
                    outcome.Results.ToArray()),
                CancellationToken.None);
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            warning =
                "The final receipt could not be written. The receipt " +
                "written before deletion still lists every ticket, but " +
                "not their outcomes.";
        }

        if (cancelled)
        {
            throw new OperationCanceledException(
                "The deletion was cancelled. See the receipt for what " +
                "was done: " + receiptPath,
                cancellationToken);
        }

        return new DeletionRun(outcome, receiptPath, warning);
    }

    private static void RequireConfirmation(
        DeletionPlan plan,
        DeletionConfirmation confirmation)
    {
        if (confirmation.ConfirmedCount != plan.Tickets.Count)
        {
            throw new InvalidOperationException(
                "The confirmed number of tickets does not match the " +
                "plan. Nothing was deleted.");
        }

        var nonClosed = plan.NonClosed.Count;

        if ((nonClosed > 0 && confirmation.ConfirmedNonClosedCount != nonClosed)
            || (nonClosed == 0
                && confirmation.ConfirmedNonClosedCount is not (null or 0)))
        {
            throw new InvalidOperationException(
                "The confirmed number of tickets that are not closed " +
                "does not match the plan. Nothing was deleted.");
        }
    }

    /// <summary>
    /// Deletes one batch. Returns true when the run must stop.
    /// </summary>
    private async Task<bool> RunBatchAsync(
        IReadOnlyList<long> batch,
        int number,
        int batchCount,
        Dictionary<long, TicketDeletionResult> results,
        Action<DeletionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ZendeskDeleteJob job;

        try
        {
            job =
                await _provider.StartDeleteAsync(
                    batch,
                    cancellationToken);
        }
        catch (ZendeskApiException exception)
            when (IsClearRejection(exception.StatusCode))
        {
            // Rejected before anything was queued: nothing was deleted.
            MarkUnstarted(
                batch,
                results,
                TicketDeletionState.Failed,
                exception.Message,
                null);

            return true;
        }
        catch (ZendeskAuthenticationException exception)
        {
            // The token was needed before anything could be sent.
            MarkUnstarted(
                batch,
                results,
                TicketDeletionState.Failed,
                "Not signed in, so nothing was sent: " + exception.Message,
                null);

            return true;
        }
        catch (Exception exception)
            when (IsUnclear(exception, cancellationToken))
        {
            MarkUnstarted(
                batch,
                results,
                TicketDeletionState.Unknown,
                "The request did not complete clearly (" +
                exception.GetType().Name + "). Check the deleted " +
                "tickets in Zendesk.",
                null);

            return true;
        }

        var polls = 0;

        while (job.IsRunning)
        {
            progress?.Invoke(
                new DeletionProgress(
                    DeletionProgressPhase.WaitingForJob,
                    number,
                    batchCount,
                    batch.Count,
                    job.Status,
                    job.Progress,
                    job.Total,
                    null));

            if (polls++ >= _maxPolls)
            {
                MarkUnstarted(
                    batch,
                    results,
                    TicketDeletionState.Unknown,
                    "The job was still running when the wait ended. " +
                    "Check the deleted tickets in Zendesk.",
                    job.JobId);

                return true;
            }

            try
            {
                await _delay(_pollInterval, cancellationToken);

                job =
                    await _provider.GetJobAsync(
                        job.JobId,
                        cancellationToken);
            }
            catch (Exception exception)
                when (exception is ZendeskApiException
                          or ZendeskAuthenticationException
                          or HttpRequestException
                          or InvalidOperationException
                          or JsonException
                      || (exception is OperationCanceledException
                          && !cancellationToken.IsCancellationRequested))
            {
                // The job was started, so the result is not known.
                MarkUnstarted(
                    batch,
                    results,
                    TicketDeletionState.Unknown,
                    "The job's result could not be read (" +
                    exception.GetType().Name + "). Check the deleted " +
                    "tickets in Zendesk.",
                    job.JobId);

                return true;
            }
        }

        return Apply(batch, job, results);
    }

    /// <summary>
    /// Records a finished job's per-ticket results. Returns true when the
    /// run must stop.
    /// </summary>
    private static bool Apply(
        IReadOnlyList<long> batch,
        ZendeskDeleteJob job,
        Dictionary<long, TicketDeletionResult> results)
    {
        var reported =
            job.Results
                .GroupBy(result => result.TicketId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last());

        var jobFailed = job.Status == ZendeskDeleteJob.Failed;
        var stop = jobFailed;

        if (job.Status is not (ZendeskDeleteJob.Completed
            or ZendeskDeleteJob.Failed))
        {
            MarkUnstarted(
                batch,
                results,
                TicketDeletionState.Unknown,
                "Zendesk reported an unrecognised job status. Check " +
                "the deleted tickets in Zendesk.",
                job.JobId);

            return true;
        }

        foreach (var id in batch)
        {
            if (reported.TryGetValue(id, out var result)
                && result.Success == true)
            {
                results[id] =
                    new TicketDeletionResult(
                        id,
                        TicketDeletionState.Deleted,
                        null,
                        job.JobId);
            }
            else if (reported.TryGetValue(id, out result)
                     && result.Success == false
                     && !jobFailed)
            {
                results[id] =
                    new TicketDeletionResult(
                        id,
                        TicketDeletionState.Failed,
                        "Zendesk did not delete this ticket" +
                        (string.IsNullOrWhiteSpace(result.Status)
                            ? "."
                            : " (" + result.Status + ")."),
                        job.JobId);
            }
            else
            {
                // No result, or a job that failed: not known.
                results[id] =
                    new TicketDeletionResult(
                        id,
                        TicketDeletionState.Unknown,
                        jobFailed
                            ? "The deletion job failed" +
                              (string.IsNullOrWhiteSpace(job.Message)
                                  ? "."
                                  : ": " + job.Message)
                            : "Zendesk reported no result for this ticket.",
                        job.JobId);

                stop = true;
            }
        }

        return stop;
    }

    private static void MarkUnstarted(
        IReadOnlyList<long> batch,
        Dictionary<long, TicketDeletionResult> results,
        TicketDeletionState state,
        string detail,
        string? jobId)
    {
        foreach (var id in batch)
        {
            results[id] =
                new TicketDeletionResult(id, state, detail, jobId);
        }
    }

    /// <summary>
    /// A 4xx answer to the deletion request means Zendesk refused it
    /// before queuing anything.
    /// </summary>
    private static bool IsClearRejection(
        HttpStatusCode status) =>
        (int)status is >= 400 and < 500
        && status != HttpStatusCode.RequestTimeout;

    private static bool IsUnclear(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception is ZendeskApiException
            or HttpRequestException
            or InvalidOperationException
            or JsonException
        || (exception is OperationCanceledException
            && !cancellationToken.IsCancellationRequested);
}
