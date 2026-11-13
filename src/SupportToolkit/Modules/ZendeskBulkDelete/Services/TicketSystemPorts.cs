using SupportToolkit.Modules.ZendeskBulkDelete.Models;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Sign-in, search and name lookups in the ticketing system, as the
/// module's own types. Read-only: nothing here can delete.
/// </summary>
public interface ITicketDirectory
{
    /// <summary>Interactive sign-in; needs an operator at the browser.</summary>
    Task SignInAsync(
        CancellationToken cancellationToken);

    Task<DeletionOperator> GetCurrentUserAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The exact query the ticketing system runs for these criteria,
    /// shown to the operator. Throws ArgumentException when a term
    /// cannot be searched for.
    /// </summary>
    string DescribeQuery(
        TicketSearchCriteria criteria);

    Task<TicketSearchResult> SearchAsync(
        TicketSearchCriteria criteria,
        CancellationToken cancellationToken);

    /// <summary>
    /// Names of the given organizations; an id the system does not return
    /// is absent. Throws DirectoryLookupException when the names could
    /// not be read.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> GetOrganizationNamesAsync(
        IEnumerable<long> organizationIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Email addresses of the given users; as GetOrganizationNamesAsync.
    /// </summary>
    Task<IReadOnlyDictionary<long, string>> GetUserEmailsAsync(
        IEnumerable<long> userIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// Ticket deletion in the ticketing system. The system deletes
/// asynchronously: a request starts a job, and the outcome is read from
/// the job. Failures are reported as DeletionRequestException.
/// </summary>
public interface ITicketDeleter
{
    /// <summary>Sends exactly these IDs, once, and never retries.</summary>
    Task<DeleteJob> StartDeleteAsync(
        IReadOnlyList<long> ticketIds,
        CancellationToken cancellationToken);

    Task<DeleteJob> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken);
}

public enum DeleteJobState
{
    Running,
    Completed,
    Failed,

    /// <summary>A status the module does not know: never read as done.</summary>
    Unrecognised
}

/// <summary>
/// What a job reported for one ticket. Success is null when it did not say.
/// </summary>
public sealed record DeleteJobResult(
    long TicketId,
    bool? Success,
    string? Status);

/// <summary>
/// A deletion job as the ticketing system reports it. Status is the
/// system's own word for the state, for display.
/// </summary>
public sealed record DeleteJob(
    string JobId,
    DeleteJobState State,
    string Status,
    int? Progress,
    int? Total,
    string? Message,
    IReadOnlyList<DeleteJobResult> Results)
{
    public bool IsRunning => State == DeleteJobState.Running;
}

public enum DeletionRequestFailure
{
    /// <summary>Refused before anything was queued: nothing was deleted.</summary>
    Rejected,

    /// <summary>No valid sign-in, so nothing was sent.</summary>
    NotSignedIn,

    /// <summary>The outcome is not known.</summary>
    Unclear
}

/// <summary>
/// A deletion request or job read that failed. SourceType names the
/// underlying failure (for example HttpRequestException) for the receipt,
/// without exposing what the system returned.
/// </summary>
public sealed class DeletionRequestException : Exception
{
    public DeletionRequestException(
        DeletionRequestFailure failure,
        string sourceType,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
        SourceType = sourceType;
    }

    public DeletionRequestFailure Failure { get; }

    public string SourceType { get; }
}

/// <summary>
/// The sign-in did not complete or has expired. The operator must sign in
/// again; nothing else is wrong.
/// </summary>
public sealed class SignInRequiredException : Exception
{
    public SignInRequiredException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Names or emails behind the search results could not be read. The
/// preview continues with those groupings marked unknown.
/// </summary>
public sealed class DirectoryLookupException : Exception
{
    public DirectoryLookupException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
