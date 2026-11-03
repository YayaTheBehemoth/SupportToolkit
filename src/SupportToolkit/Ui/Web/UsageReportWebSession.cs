using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Ui.Web;

/// <summary>
/// Runs the usage report for the web UI. The run is a task owned by this
/// object, not by a browser tab, so reloading or closing the tab does not
/// abandon it; any page re-attaches to the state and the progress feed.
///
/// The rules are all in UsageReportSession and the module behind it. This
/// class only adds running in the background, cancelling, error text and
/// telling pages that something changed. The report is held in memory and
/// discarded by the next run or when the program exits.
/// </summary>
public sealed class UsageReportWebSession
{
    private readonly object _lock = new();

    private CancellationTokenSource? _cancellation;

    public UsageReportWebSession(
        UsageReportSession report,
        ProgressFeed progress)
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
    }

    public UsageReportSession Report { get; }

    public ProgressFeed Progress { get; }

    public event Action? Changed;

    public ReviewRunState State { get; private set; } = ReviewRunState.Idle;

    /// <summary>Why the last run failed. Safe to show: no secrets.</summary>
    public string? Error { get; private set; }

    public bool IsRunning => State == ReviewRunState.Running;

    /// <summary>
    /// Starts a run unless one is running. Returns false when one is.
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
            await Report.GenerateAsync(cancellationToken);

            State = ReviewRunState.Finished;
        }
        catch (OperationCanceledException)
        {
            State = ReviewRunState.Cancelled;
        }
        catch (Exception exception)
        {
            State = ReviewRunState.Failed;
            Error = ConsoleErrorFormatter.Format(exception);
        }

        Changed?.Invoke();
    }
}
