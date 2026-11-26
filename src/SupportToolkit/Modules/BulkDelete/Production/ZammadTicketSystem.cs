using System.Net;
using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BulkDelete.Models;
using SupportToolkit.Modules.BulkDelete.Services;
using SupportToolkit.Providers.Zammad.Configuration;
using SupportToolkit.Providers.Zammad.Deletion;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Modules.BulkDelete.Production;

/// <summary>
/// What deleting in Zammad is like. Zammad deletes one ticket per request,
/// at once and permanently: there is no trash and no restore, no waiting
/// time after closing, and no archive. The saved deletion token signs in;
/// no browser is involved.
/// </summary>
public static class ZammadDeletionRules
{
    public static TicketSystemRules Rules { get; } =
        new()
        {
            SystemName = "Zammad",
            ReceiptPrefix = "zammad",
            DisplayInstance = instance => instance,
            BatchSize = 1,
            SignsInWithBrowser = false,
            RunsJobs = false,
            DescribeRequest = (_, first, _) => $"DELETE /api/v1/tickets/{first}"
        };

    /// <summary>
    /// The instance's name in plans and receipts: host and port
    /// ("helpdesk.example.com", "127.0.0.1:8100").
    /// </summary>
    public static string InstanceOf(Uri baseUri) =>
        baseUri.IsDefaultPort ? baseUri.Host : baseUri.Authority;
}

/// <summary>
/// Zammad as the ticketing system: sign-in with the deletion token, search
/// and lookups, mapped to the module's types. A refused token becomes
/// SignInRequiredException wherever it happens. Organization names and
/// requester emails come with the search, so the lookups read no more.
/// </summary>
public sealed class ZammadTicketDirectory : ITicketDirectory
{
    private readonly ZammadDeletionProvider _provider;

    private IReadOnlyDictionary<long, string> _organizations =
        new Dictionary<long, string>();

    private IReadOnlyDictionary<long, string> _emails =
        new Dictionary<long, string>();

    public ZammadTicketDirectory(ZammadDeletionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public TicketSystemRules Rules => ZammadDeletionRules.Rules;

    /// <summary>Checks the deletion token by reading its user.</summary>
    public Task SignInAsync(CancellationToken cancellationToken) =>
        GetCurrentUserAsync(cancellationToken);

    public Task<DeletionOperator> GetCurrentUserAsync(
        CancellationToken cancellationToken) =>
        SignInGuard(async () =>
        {
            var user = await _provider.GetCurrentUserAsync(cancellationToken);

            return new DeletionOperator(user.Id, user.Name, user.Email, user.Roles);
        });

    public string DescribeQuery(TicketSearchCriteria criteria) =>
        ZammadSearchQuery.Build(ToZammad(criteria));

    public Task<TicketSearchResult> SearchAsync(
        TicketSearchCriteria criteria,
        CancellationToken cancellationToken) =>
        SignInGuard(async () =>
        {
            var result =
                await _provider.SearchAsync(ToZammad(criteria), cancellationToken);

            _organizations = result.OrganizationNames;
            _emails = result.CustomerEmails;

            return new TicketSearchResult(
                result.Tickets
                    .Select(ticket => new TicketSummary(
                        ticket.Id,
                        ticket.State,
                        ticket.Title,
                        ticket.CustomerId,
                        ticket.OrganizationId,
                        ticket.CreatedAt,
                        ticket.UpdatedAt,
                        null,
                        ticket.Number,
                        _provider.TicketLink(ticket.Id)))
                    .ToArray(),
                result.ReportedCount,
                result.DuplicateCount,
                result.IsComplete,
                result.IncompleteReason);
        });

    public Task<IReadOnlyDictionary<long, string>> GetOrganizationNamesAsync(
        IEnumerable<long> organizationIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(Pick(_organizations, organizationIds));

    public Task<IReadOnlyDictionary<long, string>> GetUserEmailsAsync(
        IEnumerable<long> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult(Pick(_emails, userIds));

    public static ZammadSearchCriteria ToZammad(TicketSearchCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        return new ZammadSearchCriteria(
            criteria.Organization,
            criteria.RequesterEmail,
            criteria.EmailDomain,
            criteria.FreeText,
            criteria.SenderName,
            criteria.TicketNumber);
    }

    private static IReadOnlyDictionary<long, string> Pick(
        IReadOnlyDictionary<long, string> known,
        IEnumerable<long> ids) =>
        ids.Distinct()
            .Where(known.ContainsKey)
            .ToDictionary(id => id, id => known[id]);

    private static async Task<T> SignInGuard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ZammadApiException exception)
            when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new SignInRequiredException(
                "Zammad refused the deletion token (HTTP 401): it is " +
                "wrong, inactive or expired.",
                exception);
        }
    }
}

/// <summary>
/// Zammad's deletion, mapped to the module's job model. Zammad deletes one
/// ticket per request and its answer is the outcome, so each request
/// returns a job that has already finished:
///  - 200 deleted; 404 not deleted (no such ticket);
///  - 401 nothing sent that counted: not signed in;
///  - another 4xx (403 without the admin permission): refused;
///  - anything else, a transport failure or a timeout: unclear.
/// Cancellation by the caller is never translated.
/// </summary>
public sealed class ZammadTicketDeleter : ITicketDeleter
{
    private readonly ZammadDeletionProvider _provider;

    public ZammadTicketDeleter(ZammadDeletionProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<DeleteJob> StartDeleteAsync(
        IReadOnlyList<long> ticketIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        if (ticketIds.Count != 1)
        {
            throw new ArgumentException(
                "Zammad deletes one ticket per request.",
                nameof(ticketIds));
        }

        var id = ticketIds[0];

        ZammadDeleteOutcome outcome;

        try
        {
            outcome = await _provider.DeleteTicketAsync(id, cancellationToken);
        }
        catch (Exception exception)
            when (exception is HttpRequestException
                      or InvalidOperationException
                      or JsonException
                  || (exception is OperationCanceledException
                      && !cancellationToken.IsCancellationRequested))
        {
            throw new DeletionRequestException(
                DeletionRequestFailure.Unclear,
                exception.GetType().Name,
                exception.Message,
                exception);
        }

        return outcome.Status switch
        {
            ZammadDeleteStatus.Deleted =>
                Finished(id, true, "deleted"),

            ZammadDeleteStatus.NotFound =>
                Finished(id, false, "not found: already deleted, or no such ticket"),

            ZammadDeleteStatus.TokenRefused =>
                throw new DeletionRequestException(
                    DeletionRequestFailure.NotSignedIn,
                    "HTTP 401",
                    "Zammad refused the deletion token: " + outcome.Detail),

            ZammadDeleteStatus.Refused =>
                throw new DeletionRequestException(
                    DeletionRequestFailure.Rejected,
                    $"HTTP {outcome.HttpStatus}",
                    $"Zammad refused the deletion (HTTP {outcome.HttpStatus}): " +
                    outcome.Detail),

            _ =>
                throw new DeletionRequestException(
                    DeletionRequestFailure.Unclear,
                    $"HTTP {outcome.HttpStatus}",
                    $"Zammad answered HTTP {outcome.HttpStatus}: {outcome.Detail}")
        };
    }

    /// <summary>Never called: every job this deleter returns has finished.</summary>
    public Task<DeleteJob> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken) =>
        throw new DeletionRequestException(
            DeletionRequestFailure.Unclear,
            nameof(InvalidOperationException),
            "Zammad deletions have no job to read.");

    private static DeleteJob Finished(long id, bool deleted, string status) =>
        new(
            "ticket-" + id,
            DeleteJobState.Completed,
            status,
            1,
            1,
            null,
            [new DeleteJobResult(id, deleted, status)]);
}

/// <summary>
/// Owns the HTTP clients of one Zammad bulk-delete session, built from the
/// profile's Zammad connection and its deletion token (never the token
/// that creates tickets). The client that can delete is built only when
/// deletion is enabled, and only the executor uses it.
/// </summary>
public sealed class ZammadBulkDeleteSession : IDisposable
{
    private readonly HttpClient _readClient;
    private readonly HttpClient? _deleteClient;

    private ZammadBulkDeleteSession(
        HttpClient readClient,
        HttpClient? deleteClient,
        BulkDeleteWorkflow workflow,
        DeletionExecutor? executor,
        string instance)
    {
        _readClient = readClient;
        _deleteClient = deleteClient;
        Workflow = workflow;
        Executor = executor;
        Instance = instance;
    }

    public BulkDeleteWorkflow Workflow { get; }

    public DeletionExecutor? Executor { get; }

    public string Instance { get; }

    public BulkDeleteReviewSession CreateReviewSession(
        TimeProvider? timeProvider = null) =>
        new(
            Instance,
            Workflow.SignInAsync,
            Workflow.PreviewAsync,
            Executor,
            timeProvider,
            Workflow.DescribeQuery);

    /// <summary>
    /// Builds the session. Nothing is sent until the workflow asks.
    /// </summary>
    public static ZammadBulkDeleteSession Create(
        ResolvedZammadDeletionConnection connection,
        OperationalLogger? logger = null,
        bool enableDeletion = false,
        string? receiptDirectory = null,
        Func<HttpClient>? createClient = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var options =
            ZammadOptions.Create(connection.BaseUrl, connection.DeletionToken);

        createClient ??= () => ZammadHttpPipeline.Create();

        // Refuses a receipt folder inside a git working tree, before
        // anything is read.
        var receipts =
            enableDeletion
                ? new DeletionReceiptStore(
                    receiptDirectory ?? DeletionReceiptStore.DefaultDirectory)
                : null;

        var readClient = createClient();

        var workflow =
            new BulkDeleteWorkflow(
                new ZammadTicketDirectory(
                    new ZammadDeletionProvider(
                        new ZammadApiClient(readClient, options, logger))));

        HttpClient? deleteClient = null;
        DeletionExecutor? executor = null;

        if (receipts is not null)
        {
            deleteClient = createClient();

            executor =
                new DeletionExecutor(
                    new ZammadTicketDeleter(
                        new ZammadDeletionProvider(
                            new ZammadApiClient(deleteClient, options, logger))),
                    receipts);
        }

        return new ZammadBulkDeleteSession(
            readClient,
            deleteClient,
            workflow,
            executor,
            ZammadDeletionRules.InstanceOf(options.BaseUri));
    }

    public void Dispose()
    {
        _deleteClient?.Dispose();
        _readClient.Dispose();
    }
}
