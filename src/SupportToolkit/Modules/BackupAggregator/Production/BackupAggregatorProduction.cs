using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Services;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.Zendesk.Tickets;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// How the Backup Aggregator's workflows reach the real systems: the
/// Acronis review session and Zendesk ticket submission, built from the
/// active profile's connections. The workflows decide what to do; this is
/// only how it is done against Acronis and Zendesk.
///
/// Local configuration is always read before any session is created, so a
/// missing or invalid setting fails before any external request.
/// </summary>
public static class BackupAggregatorProduction
{
    public static async Task<BackupAggregatorProductionSession> CreateSessionAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var resolvedConnection =
            await configurationResolver
                .GetAcronisConnectionAsync(
                    cancellationToken
                );

        var acronisOptions =
            AcronisOptions.Create(
                resolvedConnection.DatacenterUrl,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        var wait =
            await InProgressWaitSetup.LoadAsync(
                configurationResolver,
                BackupAggregatorModule.ModuleCommand,
                runMode,
                cancellationToken
            );

        var logger =
            OperationalLogger.FromEnvironment();

        return BackupAggregatorProductionSession
            .Create(
                acronisOptions,
                logger,
                wait,
                progress
            );
    }

    /// <summary>
    /// The configured Zendesk destination, resolved and validated without
    /// calling Zendesk or exposing credentials.
    /// </summary>
    public static async Task<ZendeskTicketDestinationPreview> GetTicketDestinationAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        CancellationToken cancellationToken)
    {
        var connection =
            await configurationResolver.GetZendeskConnectionAsync(
                cancellationToken);

        var options = ZendeskOptions.Create(
            connection.Subdomain,
            connection.ClientId,
            connection.ClientSecret);

        return new ZendeskTicketDestinationPreview(
            options.BaseUri.Host,
            connection.DestinationName,
            connection.GroupId);
    }

    /// <summary>
    /// Submits a draft to the configured Zendesk destination. With a
    /// confirmed destination, fails closed if the configuration has changed
    /// since it was confirmed.
    /// </summary>
    public static async Task<CreatedTicket> SubmitTicketDraftAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        TicketDraft draft,
        ZendeskTicketDestinationPreview? confirmedDestination,
        CancellationToken cancellationToken)
    {
        /*
         * Resolve the destination before creating the HTTP session.
         * Missing or invalid routing configuration must fail locally.
         */
        var resolvedConnection =
            await configurationResolver
                .GetZendeskConnectionAsync(
                    cancellationToken
                );

        var zendeskOptions =
            ZendeskOptions.Create(
                resolvedConnection.Subdomain,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        if (confirmedDestination is not null)
        {
            var currentDestination = new ZendeskTicketDestinationPreview(
                zendeskOptions.BaseUri.Host,
                resolvedConnection.DestinationName,
                resolvedConnection.GroupId);

            if (!currentDestination.IsSameAs(confirmedDestination))
            {
                throw new InvalidOperationException(
                    "The Zendesk destination changed since confirmation. " +
                    "No ticket was submitted. Review the destination and try again.");
            }
        }

        /*
         * Include the actual Zendesk instance and group in the retry
         * identity, rather than the profile or destination display name.
         *
         * Renaming a profile must not alter ticket identity.
         */
        var destinationIdentity =
            string.Join(
                ':',
                "zendesk",
                resolvedConnection.Subdomain.ToLowerInvariant(),
                resolvedConnection.GroupId.ToString(
                    CultureInfo.InvariantCulture
                )
            );

        var idempotencyKey =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft,
                destinationIdentity
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            TicketingProductionSession.Create(
                zendeskOptions,
                resolvedConnection.GroupId,
                new ZendeskTicketIdentity(
                    resolvedConnection.SubmitterId,
                    resolvedConnection.RequesterId
                ),
                logger
            );

        return await session.TicketProvider
            .CreateTicketAsync(
                draft,
                idempotencyKey,
                cancellationToken
            );
    }
}

/// <summary>
/// The scoped review's default sources in production: Business Central
/// evidence and customer names, sharing one Business Central sign-in, and
/// the Acronis review session.
/// </summary>
public sealed class ScopedReviewProductionSources
{
    private readonly SupportToolkitConfigurationResolver _configurationResolver;
    private readonly ReviewRunMode _runMode;
    private readonly Action<string>? _progress;

    // One Business Central bundle per workflow, so the evidence and
    // the names share a single sign-in.
    private Task<BusinessCentralProviderBundle>? _businessCentralBundle;

    public ScopedReviewProductionSources(
        SupportToolkitConfigurationResolver configurationResolver,
        ReviewRunMode runMode,
        Action<string>? progress)
    {
        _configurationResolver = configurationResolver;
        _runMode = runMode;
        _progress = progress;
    }

    public async Task<ISmartSubscriptionEvidenceSource>
        CreateEvidenceSourceAsync(
            CancellationToken cancellationToken)
    {
        var bundle =
            await GetBusinessCentralBundleAsync(cancellationToken);

        return new BusinessCentralSmartSubscriptionEvidenceSource(
            bundle.Provider,
            BusinessCentralSourceContext.From(bundle.Options));
    }

    public async Task<ICustomerNameSource>
        CreateNameSourceAsync(
            CancellationToken cancellationToken)
    {
        var bundle =
            await GetBusinessCentralBundleAsync(cancellationToken);

        return new BusinessCentralCustomerNameSource(
            bundle.Provider);
    }

    public async Task<ScopedInventoryRuntime>
        CreateInventoryRuntimeAsync(
            CancellationToken cancellationToken)
    {
        var session =
            await BackupAggregatorProduction.CreateSessionAsync(
                _configurationResolver,
                _runMode,
                _progress,
                cancellationToken);

        return new ScopedInventoryRuntime(
            session.ReviewService,
            session);
    }

    private Task<BusinessCentralProviderBundle>
        GetBusinessCentralBundleAsync(
            CancellationToken cancellationToken)
    {
        return _businessCentralBundle ??=
            BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);
    }
}
