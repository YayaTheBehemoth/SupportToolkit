using System.Net;
using System.Text.Json;
using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Modules.ZendeskBulkDelete.Services;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Deleting;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Production;

/// <summary>
/// Zendesk as the ticketing system: sign-in, search and lookups, mapped
/// to the module's types. A failed or expired sign-in becomes
/// SignInRequiredException wherever it happens; a failed name lookup
/// (anything but a sign-in problem) becomes DirectoryLookupException.
/// </summary>
public sealed class ZendeskTicketDirectory : ITicketDirectory
{
    private readonly Func<CancellationToken, Task> _signIn;
    private readonly ZendeskTicketSearchProvider _search;
    private readonly ZendeskDirectoryProvider _directory;

    public ZendeskTicketDirectory(
        Func<CancellationToken, Task> signIn,
        ZendeskTicketSearchProvider search,
        ZendeskDirectoryProvider directory)
    {
        _signIn = signIn ?? throw new ArgumentNullException(nameof(signIn));
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    }

    public Task SignInAsync(
        CancellationToken cancellationToken) =>
        SignInGuard(() => _signIn(cancellationToken));

    public Task<DeletionOperator> GetCurrentUserAsync(
        CancellationToken cancellationToken) =>
        SignInGuard(async () =>
        {
            var user = await _directory.GetCurrentUserAsync(cancellationToken);

            return new DeletionOperator(user.Id, user.Name, user.Email, user.Role);
        });

    public string DescribeQuery(
        TicketSearchCriteria criteria) =>
        ZendeskSearchQuery.Build(ToZendesk(criteria));

    public Task<TicketSearchResult> SearchAsync(
        TicketSearchCriteria criteria,
        CancellationToken cancellationToken) =>
        SignInGuard(async () =>
        {
            var result =
                await _search.SearchTicketsAsync(
                    ToZendesk(criteria),
                    cancellationToken);

            return new TicketSearchResult(
                result.Tickets
                    .Select(ticket => new TicketSummary(
                        ticket.Id,
                        ticket.Status,
                        ticket.Subject,
                        ticket.RequesterId,
                        ticket.OrganizationId,
                        ticket.CreatedAt,
                        ticket.UpdatedAt,
                        ticket.Description))
                    .ToArray(),
                result.ReportedCount,
                result.DuplicateCount,
                result.IsComplete,
                result.IncompleteReason);
        });

    public Task<IReadOnlyDictionary<long, string>> GetOrganizationNamesAsync(
        IEnumerable<long> organizationIds,
        CancellationToken cancellationToken) =>
        LookupGuard(
            () => _directory.GetOrganizationNamesAsync(
                organizationIds,
                cancellationToken));

    public Task<IReadOnlyDictionary<long, string>> GetUserEmailsAsync(
        IEnumerable<long> userIds,
        CancellationToken cancellationToken) =>
        LookupGuard(
            () => _directory.GetUserEmailsAsync(
                userIds,
                cancellationToken));

    public static ZendeskSearchCriteria ToZendesk(
        TicketSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        return new ZendeskSearchCriteria(
            criteria.Organization,
            criteria.RequesterEmail,
            criteria.EmailDomain,
            criteria.FreeText,
            criteria.SenderName);
    }

    private static async Task SignInGuard(
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ZendeskAuthenticationException exception)
        {
            throw new SignInRequiredException(exception.Message, exception);
        }
    }

    private static async Task<T> SignInGuard<T>(
        Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ZendeskAuthenticationException exception)
        {
            throw new SignInRequiredException(exception.Message, exception);
        }
    }

    private static async Task<IReadOnlyDictionary<long, string>> LookupGuard(
        Func<Task<IReadOnlyDictionary<long, string>>> lookup)
    {
        try
        {
            return await SignInGuard(lookup);
        }
        catch (Exception exception)
            when (IsLookupFailure(exception))
        {
            throw new DirectoryLookupException(exception.Message, exception);
        }
    }

    // Sign-in problems (a 401, or a failed sign-in) are never swallowed
    // as a lookup gap.
    private static bool IsLookupFailure(
        Exception exception) =>
        exception is ZendeskApiException
            {
                StatusCode: not HttpStatusCode.Unauthorized
            }
            or HttpRequestException
            or InvalidOperationException
            or JsonException;
}

/// <summary>
/// Zendesk's bulk deletion endpoint and its jobs, mapped to the module's
/// types. Zendesk's failures are classified here, once:
///  - starting a job: a 4xx (except 408) is a clear rejection (nothing was
///    queued); a failed sign-in means nothing was sent; any other API,
///    transport, contract or timeout failure leaves the outcome unclear;
///  - reading a job: any such failure leaves the outcome unclear.
/// Cancellation by the caller is never translated.
/// </summary>
public sealed class ZendeskTicketDeleter : ITicketDeleter
{
    private readonly ZendeskTicketDeletionProvider _provider;

    public ZendeskTicketDeleter(
        ZendeskTicketDeletionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<DeleteJob> StartDeleteAsync(
        IReadOnlyList<long> ticketIds,
        CancellationToken cancellationToken)
    {
        try
        {
            return ToJob(
                await _provider.StartDeleteAsync(
                    ticketIds,
                    cancellationToken));
        }
        catch (ZendeskApiException exception)
            when (IsClearRejection(exception.StatusCode))
        {
            throw Failure(DeletionRequestFailure.Rejected, exception);
        }
        catch (ZendeskAuthenticationException exception)
        {
            throw Failure(DeletionRequestFailure.NotSignedIn, exception);
        }
        catch (Exception exception)
            when (IsUnclear(exception, cancellationToken))
        {
            throw Failure(DeletionRequestFailure.Unclear, exception);
        }
    }

    public async Task<DeleteJob> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            return ToJob(
                await _provider.GetJobAsync(
                    jobId,
                    cancellationToken));
        }
        catch (Exception exception)
            when (exception is ZendeskAuthenticationException
                  || IsUnclear(exception, cancellationToken))
        {
            throw Failure(DeletionRequestFailure.Unclear, exception);
        }
    }

    private static DeleteJob ToJob(
        ZendeskDeleteJob job) =>
        new(
            job.JobId,
            job.Status switch
            {
                ZendeskDeleteJob.Queued or ZendeskDeleteJob.Working => DeleteJobState.Running,
                ZendeskDeleteJob.Completed => DeleteJobState.Completed,
                ZendeskDeleteJob.Failed => DeleteJobState.Failed,
                _ => DeleteJobState.Unrecognised
            },
            job.Status,
            job.Progress,
            job.Total,
            job.Message,
            job.Results
                .Select(result => new DeleteJobResult(
                    result.TicketId,
                    result.Success,
                    result.Status))
                .ToArray());

    private static DeletionRequestException Failure(
        DeletionRequestFailure failure,
        Exception exception) =>
        new(
            failure,
            exception.GetType().Name,
            exception.Message,
            exception);

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
