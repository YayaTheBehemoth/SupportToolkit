using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;
using SupportToolkit.Providers.Zendesk.Deletion.Deleting;
using SupportToolkit.Providers.Zendesk.Deletion.Search;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Owns the HTTP clients and the sign-in of one bulk-delete session,
/// built from the profile's Zendesk deletion connection.
///
/// It never reads the ticketing connection, its secret or its ticket
/// destination. The sign-in is in memory only and ends with the session.
///
/// The read-only preview pipeline can never delete. The pipeline that can
/// is built only when deletion is explicitly enabled, and only the
/// executor uses it.
/// </summary>
public sealed class ZendeskBulkDeleteSession : IDisposable
{
    private readonly HttpClient _signInClient;
    private readonly HttpClient _apiClient;
    private readonly HttpClient? _deleterClient;

    private ZendeskBulkDeleteSession(
        HttpClient signInClient,
        HttpClient apiClient,
        HttpClient? deleterClient,
        ZendeskBulkDeleteWorkflow workflow,
        ZendeskDeletionExecutor? executor,
        ZendeskDeletionOptions options)
    {
        _signInClient = signInClient;
        _apiClient = apiClient;
        _deleterClient = deleterClient;
        Workflow = workflow;
        Executor = executor;
        Options = options;
    }

    public ZendeskBulkDeleteWorkflow Workflow { get; }

    /// <summary>
    /// Null unless deletion was enabled when the session was created.
    /// </summary>
    public ZendeskDeletionExecutor? Executor { get; }

    public ZendeskDeletionOptions Options { get; }

    public static async Task<ZendeskBulkDeleteSession> CreateAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        OperationalLogger? logger = null,
        bool enableDeletion = false,
        string? receiptDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configurationResolver);

        var connection =
            await configurationResolver
                .GetZendeskDeletionConnectionAsync(cancellationToken);

        var options =
            ZendeskDeletionOptions.Create(
                connection.Subdomain,
                connection.ClientId);

        // Refuses a receipt folder inside a git working tree, before
        // anything is read or signed in.
        var receipts =
            enableDeletion
                ? new DeletionReceiptStore(
                    receiptDirectory ?? DeletionReceiptStore.DefaultDirectory)
                : null;

        var signInClient =
            ZendeskDeletionHttpPipeline.CreateSignInClient(options);

        var tokenSource =
            new ZendeskPkceTokenSource(
                signInClient,
                options,
                new SystemBrowserLauncher(),
                () => ZendeskLoopbackReceiver.Start(),
                logger: logger);

        var apiClient =
            ZendeskDeletionHttpPipeline.Create(
                options,
                tokenSource);

        var client =
            new ZendeskDeletionApiClient(
                apiClient,
                options,
                logger);

        var workflow =
            new ZendeskBulkDeleteWorkflow(
                tokenSource.SignInAsync,
                new ZendeskTicketSearchProvider(client),
                new ZendeskDirectoryProvider(client));

        HttpClient? deleterClient = null;
        ZendeskDeletionExecutor? executor = null;

        if (receipts is not null)
        {
            deleterClient =
                ZendeskDeletionHttpPipeline.CreateDeleter(
                    options,
                    tokenSource);

            executor =
                new ZendeskDeletionExecutor(
                    new ZendeskTicketDeletionProvider(
                        new ZendeskDeletionApiClient(
                            deleterClient,
                            options,
                            logger)),
                    receipts);
        }

        return new ZendeskBulkDeleteSession(
            signInClient,
            apiClient,
            deleterClient,
            workflow,
            executor,
            options);
    }

    public void Dispose()
    {
        _deleterClient?.Dispose();
        _apiClient.Dispose();
        _signInClient.Dispose();
    }
}
