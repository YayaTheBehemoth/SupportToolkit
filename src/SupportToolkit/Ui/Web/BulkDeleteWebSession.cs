using SupportToolkit.Core.ErrorHandling;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Search;

namespace SupportToolkit.Ui.Web;

/// <summary>Whether the active profile can be used for bulk deletion.</summary>
public sealed record BulkDeleteAvailability(
    bool IsAvailable,
    string? Subdomain,
    string? Reason);

/// <summary>
/// A bulk-delete session and whatever must be disposed with it.
/// </summary>
public sealed record BulkDeleteConnection(
    BulkDeleteReviewSession Session,
    IDisposable? Lifetime);

/// <summary>
/// Runs the Zendesk bulk delete for the web UI. Sign-in, search and
/// deletion are tasks owned by this object, not by a browser tab, so
/// closing or reloading the tab does not abandon a deletion: any page
/// re-attaches to the state.
///
/// The rules (order of steps, selection, plan, confirmations) are all in
/// BulkDeleteReviewSession. This class adds running in the background,
/// cancelling, error text and telling pages that something changed.
/// </summary>
public sealed class BulkDeleteWebSession : IDisposable
{
    private static readonly TimeSpan RefreshInterval =
        TimeSpan.FromMilliseconds(500);

    private readonly Func<CancellationToken, Task<BulkDeleteAvailability>>
        _availability;

    private readonly Func<CancellationToken, Task<BulkDeleteConnection>>
        _connect;

    private readonly object _lock = new();

    private BulkDeleteConnection? _connection;
    private CancellationTokenSource? _operation;
    private Task? _deletion;

    public BulkDeleteWebSession(
        Func<CancellationToken, Task<BulkDeleteAvailability>> availability,
        Func<CancellationToken, Task<BulkDeleteConnection>> connect)
    {
        _availability =
            availability ?? throw new ArgumentNullException(nameof(availability));

        _connect =
            connect ?? throw new ArgumentNullException(nameof(connect));
    }

    public event Action? Changed;

    /// <summary>The current work, or null before the first sign-in.</summary>
    public BulkDeleteReviewSession? Session => _connection?.Session;

    /// <summary>What the page is waiting for, or null when idle.</summary>
    public string? Busy { get; private set; }

    /// <summary>
    /// The last problem, in plain text. Cleared by the next action.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// True when the problem is that the sign-in is no longer valid.
    /// </summary>
    public bool NeedsSignIn { get; private set; }

    public bool IsDeleting =>
        Session?.Stage == BulkDeleteStage.Deleting;

    public Task<BulkDeleteAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        _availability(cancellationToken);

    // ---- Sign-in and search ----

    public bool StartSignIn() =>
        Start(
            "Waiting for you to sign in to Zendesk in the browser window " +
            "that opened...",
            async token =>
            {
                // A new connection every time, so a changed profile is
                // never signed in with the old settings.
                await DisposeConnectionAsync();

                _connection = await _connect(token);

                await _connection.Session.SignInAsync(token);
            });

    public string? SearchProblem { get; private set; }

    public bool StartSearch(ZendeskSearchCriteria criteria) =>
        Start(
            "Searching Zendesk...",
            async token =>
            {
                SearchProblem =
                    await RequireSession().SearchAsync(criteria, token);
            });

    // ---- Changes that need no waiting ----

    public string? Select(IEnumerable<long> ids) =>
        Change(session => session.Select(ids));

    public string? Deselect(IEnumerable<long> ids) =>
        Change(session => session.Deselect(ids));

    public string? SelectAll() =>
        Change(session => session.SelectAll());

    public string? ResetSelection() =>
        Change(session => session.ResetSelection());

    public string? ClearSelection() =>
        Change(session => session.ClearSelection());

    public PlanAttempt PreparePlan()
    {
        ClearMessages();

        var attempt = RequireSession().PreparePlan();

        Changed?.Invoke();

        return attempt;
    }

    /// <summary>Goes back from the plan to choosing tickets.</summary>
    public void DiscardPlan()
    {
        ClearMessages();
        RequireSession().DiscardPlan();
        Changed?.Invoke();
    }

    public void StartOver()
    {
        ClearMessages();
        RequireSession().StartOver();
        Changed?.Invoke();
    }

    /// <summary>Forget the sign-in (used when the profile changes).</summary>
    public async Task<bool> ResetAsync()
    {
        if (IsDeleting || Busy is not null)
        {
            return false;
        }

        ClearMessages();
        await DisposeConnectionAsync();
        Changed?.Invoke();

        return true;
    }

    // ---- Deletion ----

    public bool StartDelete(DeletionConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var session = RequireSession();

        lock (_lock)
        {
            if (Busy is not null || _deletion is not null)
            {
                return false;
            }

            ClearMessages();

            // Its own token: a browser tab closing does not stop it.
            _operation = new CancellationTokenSource();

            var token = _operation.Token;

            Busy = "Deleting tickets...";

            _deletion =
                Task.Run(
                    async () =>
                    {
                        try
                        {
                            var running = session.ExecuteAsync(confirmation, token);

                            // Progress is read from the session; nudge pages.
                            while (!running.IsCompleted)
                            {
                                await Task.WhenAny(
                                    running,
                                    Task.Delay(RefreshInterval));

                                Changed?.Invoke();
                            }

                            await running;
                        }
                        catch (Exception exception)
                        {
                            Error = Describe(exception);
                        }
                        finally
                        {
                            lock (_lock)
                            {
                                Busy = null;
                                _deletion = null;
                            }

                            Changed?.Invoke();
                        }
                    });
        }

        Changed?.Invoke();

        return true;
    }

    /// <summary>
    /// Stops the running work: a sign-in wait, a search or a deletion. A
    /// cancelled deletion leaves the tickets in flight as unknown.
    /// </summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _operation?.Cancel();
        }
    }

    /// <summary>
    /// Cancels a running deletion and waits for its receipt to be
    /// written, for a clean exit.
    /// </summary>
    public async Task CancelAndWaitAsync(TimeSpan timeout)
    {
        Task? deletion;

        lock (_lock)
        {
            _operation?.Cancel();
            deletion = _deletion;
        }

        if (deletion is not null)
        {
            await Task.WhenAny(deletion, Task.Delay(timeout));
        }
    }

    // ---- Plumbing ----

    private bool Start(string busy, Func<CancellationToken, Task> work)
    {
        lock (_lock)
        {
            if (Busy is not null || IsDeleting)
            {
                return false;
            }

            ClearMessages();

            _operation = new CancellationTokenSource();

            var token = _operation.Token;

            Busy = busy;

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await work(token);
                    }
                    catch (OperationCanceledException)
                    {
                        Error = "That was cancelled.";
                    }
                    catch (Exception exception)
                    {
                        Error = Describe(exception);
                        NeedsSignIn =
                            exception is ZendeskAuthenticationException;
                    }
                    finally
                    {
                        lock (_lock)
                        {
                            Busy = null;
                        }

                        Changed?.Invoke();
                    }
                });
        }

        Changed?.Invoke();

        return true;
    }

    private string? Change(Func<BulkDeleteReviewSession, string?> change)
    {
        ClearMessages();

        var message = change(RequireSession());

        Changed?.Invoke();

        return message;
    }

    private BulkDeleteReviewSession RequireSession() =>
        Session ?? throw new InvalidOperationException("Sign in first.");

    private void ClearMessages()
    {
        Error = null;
        NeedsSignIn = false;
        SearchProblem = null;
    }

    private static string Describe(Exception exception) =>
        exception is ZendeskAuthenticationException
            ? "The Zendesk sign-in did not complete or has expired. " +
              ConsoleErrorFormatter.Format(exception)
            : ConsoleErrorFormatter.Format(exception);

    private Task DisposeConnectionAsync()
    {
        var connection = _connection;

        _connection = null;
        connection?.Lifetime?.Dispose();

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _operation?.Cancel();
        _connection?.Lifetime?.Dispose();
    }
}
