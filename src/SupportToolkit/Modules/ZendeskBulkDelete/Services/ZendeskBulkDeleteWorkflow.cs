using SupportToolkit.Modules.ZendeskBulkDelete.Models;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Sign in, search and preview for bulk ticket deletion. It contains no
/// console or user interface code, and no deletion: the directory it is
/// given is read-only.
///
/// Interactive only: the sign-in needs an operator at the browser.
/// </summary>
public sealed class ZendeskBulkDeleteWorkflow
{
    private readonly ITicketDirectory _directory;
    private readonly TimeProvider _timeProvider;

    public ZendeskBulkDeleteWorkflow(
        ITicketDirectory directory,
        TimeProvider? timeProvider = null)
    {
        _directory =
            directory ?? throw new ArgumentNullException(nameof(directory));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// The exact query the ticketing system runs for these criteria.
    /// Throws ArgumentException when a term cannot be searched for.
    /// </summary>
    public string DescribeQuery(
        TicketSearchCriteria criteria) =>
        _directory.DescribeQuery(criteria);

    /// <summary>
    /// Signs the operator in and returns who they are.
    /// </summary>
    public async Task<DeletionOperator> SignInAsync(
        CancellationToken cancellationToken = default)
    {
        await _directory.SignInAsync(cancellationToken);

        return await _directory.GetCurrentUserAsync(cancellationToken);
    }

    /// <summary>
    /// Searches and builds the preview. A failed name lookup does not
    /// fail the preview: the affected groupings are marked unknown and
    /// the preview says so. A failed search does fail it.
    /// </summary>
    public async Task<DeletionPreviewResult> PreviewAsync(
        DeletionOperator operatorUser,
        TicketSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operatorUser);
        ArgumentNullException.ThrowIfNull(criteria);

        var query = _directory.DescribeQuery(criteria);

        var search =
            await _directory.SearchAsync(
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
        catch (DirectoryLookupException)
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
        catch (DirectoryLookupException)
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
}
