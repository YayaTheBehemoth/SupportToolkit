using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Ui.Web;

public enum ReviewRunState
{
    Idle,
    Running,
    Finished,
    Failed,
    Cancelled
}

/// <summary>
/// Runs the monitored review for the web UI. The review is a task owned by
/// this object, not by a browser tab, so closing or reloading the tab does
/// not abandon it; any page re-attaches to the state and the progress feed.
///
/// All rules (what may be previewed, prepared or submitted) stay in
/// BackupReviewSession. This class only adds running in the background,
/// cancelling and telling pages that something changed.
/// </summary>
public sealed class AggregatorWebSession
{
    private readonly object _lock = new();

    private CancellationTokenSource? _cancellation;

    public AggregatorWebSession(
        BackupReviewSession review,
        ProgressFeed progress)
    {
        Review = review ?? throw new ArgumentNullException(nameof(review));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public BackupReviewSession Review { get; }

    public ProgressFeed Progress { get; }

    public event Action? Changed;

    public ReviewRunState State { get; private set; } = ReviewRunState.Idle;

    /// <summary>Why the last run failed. Safe to show: no secrets.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => State == ReviewRunState.Running;

    /// <summary>The plan the operator is being asked to confirm.</summary>
    public TicketSubmissionPlan? PendingPlan { get; private set; }

    public CreatedTicket? CreatedTicket { get; private set; }

    public string? SubmitError { get; private set; }

    public bool IsSubmitting { get; private set; }

    /// <summary>
    /// Starts a review unless one is running. Returns false when one is.
    /// </summary>
    public bool Start()
    {
        lock (_lock)
        {
            if (State == ReviewRunState.Running)
            {
                return false;
            }

            State = ReviewRunState.Running;
            Error = null;
            PendingPlan = null;
            CreatedTicket = null;
            SubmitError = null;
            _cancellation = new CancellationTokenSource();
        }

        Progress.Clear();
        Changed?.Invoke();

        var token = _cancellation.Token;

        _ = Task.Run(() => RunAsync(token));

        return true;
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _cancellation?.Cancel();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Review.RunReviewAsync(cancellationToken);

            State = ReviewRunState.Finished;
        }
        catch (OperationCanceledException)
        {
            State = ReviewRunState.Cancelled;
        }
        catch (Exception exception)
        {
            State = ReviewRunState.Failed;
            Error =
                Core.ErrorHandling.ConsoleErrorFormatter.Format(exception);
        }

        Changed?.Invoke();
    }

    public TicketPreparation<TicketDraft> PreviewTicket() =>
        Review.PreviewTicket();

    /// <summary>
    /// Prepares the submission for confirmation. The returned plan is
    /// what a later Submit sends; nothing else.
    /// </summary>
    public async Task<TicketPreparation<TicketSubmissionPlan>>
        PrepareSubmissionAsync()
    {
        SubmitError = null;
        CreatedTicket = null;

        var preparation = await Review.PrepareSubmissionAsync();

        PendingPlan = preparation.IsReady ? preparation.Value : null;

        Changed?.Invoke();

        return preparation;
    }

    public void CancelPendingSubmission()
    {
        PendingPlan = null;
        Changed?.Invoke();
    }

    public async Task SubmitAsync()
    {
        var plan = PendingPlan;

        if (plan is null || IsSubmitting)
        {
            return;
        }

        IsSubmitting = true;
        PendingPlan = null;
        SubmitError = null;
        Changed?.Invoke();

        try
        {
            CreatedTicket = await Review.SubmitAsync(plan);
        }
        catch (Exception exception)
        {
            SubmitError =
                Core.ErrorHandling.ConsoleErrorFormatter.Format(exception);
        }
        finally
        {
            IsSubmitting = false;
            Changed?.Invoke();
        }
    }
}
