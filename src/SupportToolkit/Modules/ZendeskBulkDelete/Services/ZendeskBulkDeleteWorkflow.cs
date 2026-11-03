using SupportToolkit.Modules.ZendeskBulkDelete.Models;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Sign in, search and preview for bulk ticket deletion. It contains no
/// console or user interface code, and no deletion: it cannot delete,
/// because the connection's transport does not allow it yet.
///
/// Interactive only: the sign-in needs an operator at the browser.
/// </summary>
public sealed class ZendeskBulkDeleteWorkflow
{
    private readonly Func<CancellationToken, Task> _signIn;
    private readonly ZendeskTicketSearchProvider _search;
    private readonly ZendeskDirectoryProvider _directory;
    private readonly TimeProvider _timeProvider;

    public ZendeskBulkDeleteWorkflow(
        Func<CancellationToken, Task> signIn,
        ZendeskTicketSearchProvider search,
        ZendeskDirectoryProvider directory,
        TimeProvider? timeProvider = null)
    {
        _signIn =
            signIn ?? throw new ArgumentNullException(nameof(signIn));

        _search =
            search ?? throw new ArgumentNullException(nameof(search));

        _directory =
            directory ?? throw new ArgumentNullException(nameof(directory));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Signs the operator in and returns who they are.
    /// </summary>
    public async Task<ZendeskOperator> SignInAsync(
        CancellationToken cancellationToken = default)
    {
        await _signIn(cancellationToken);

        return await _directory.GetCurrentUserAsync(cancellationToken);
    }

    /// <summary>
    /// Searches and builds the preview. A failed name lookup does not
    /// fail the preview: the affected groupings are marked unknown and
    /// the preview says so. A failed search does fail it.
    /// </summary>
    public async Task<DeletionPreviewResult> PreviewAsync(
        ZendeskOperator operatorUser,
        ZendeskSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorUser);
        ArgumentNullException.ThrowIfNull(criteria);

        var query = ZendeskSearchQuery.Build(criteria);

        var search =
            await _search.SearchTicketsAsync(
                criteria,
                cancellationToken);

        var notes = new List<string>();

        IReadOnlyDictionary<long, string>? organizations = null;
        IReadOnlyDictionary<long, string>? emails = null;

        try
        {
            organizations =
                await _directory.GetOrganizationNamesAsync(
                    search.Tickets
                        .Where(ticket => ticket.OrganizationId is not null)
                        .Select(ticket => ticket.OrganizationId!.Value),
                    cancellationToken);
        }
        catch (Exception exception)
            when (IsLookupFailure(exception))
        {
            notes.Add(
                "Organization names could not be read, so organizations " +
                "are shown as unknown.");
        }

        try
        {
            emails =
                await _directory.GetUserEmailsAsync(
                    search.Tickets
                        .Where(ticket => ticket.RequesterId is not null)
                        .Select(ticket => ticket.RequesterId!.Value),
                    cancellationToken);
        }
        catch (Exception exception)
            when (IsLookupFailure(exception))
        {
            notes.Add(
                "Requester emails could not be read, so requester " +
                "domains are shown as unknown.");
        }

        return new DeletionPreviewResult(
            operatorUser,
            query,
            DeletionPreviewBuilder.Build(
                search,
                organizations,
                emails,
                notes.Count == 0
                    ? null
                    : string.Join(" ", notes),
                _timeProvider.GetUtcNow()));
    }

    // Sign-in problems (a 401, or a failed sign-in) are never swallowed
    // as a lookup gap.
    private static bool IsLookupFailure(
        Exception exception) =>
        exception is ZendeskApiException
            {
                StatusCode: not System.Net.HttpStatusCode.Unauthorized
            }
            or HttpRequestException
            or InvalidOperationException
            or System.Text.Json.JsonException;
}
