using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// How the Backup Aggregator's reviews reach the real systems: the
/// Acronis review session, built from the active profile's connection.
/// The workflows decide what to do; this is only how it is done against
/// Acronis. Tickets go through TicketingTargetSink.
///
/// Local configuration is always read before any session is created, so a
/// missing or invalid setting fails before any external request.
/// </summary>
public static class BackupAggregatorProduction
{
    /// <summary>
    /// The Acronis review as a runtime any production source can use.
    /// </summary>
    public static async Task<ScopedInventoryRuntime> CreateRuntimeAsync(
        SupportToolkitConfigurationResolver configurationResolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var session =
            await CreateSessionAsync(
                configurationResolver,
                runMode,
                progress,
                cancellationToken);

        return new ScopedInventoryRuntime(session.ReviewService, session);
    }

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
        return await BackupAggregatorProduction.CreateRuntimeAsync(
            _configurationResolver,
            _runMode,
            _progress,
            cancellationToken);
    }

    private Task<BusinessCentralProviderBundle>
        GetBusinessCentralBundleAsync(
            CancellationToken cancellationToken)
    {
        return _businessCentralBundle ??=
            CreateBusinessCentralBundleAsync(
                cancellationToken);
    }

    private async Task<BusinessCentralProviderBundle>
        CreateBusinessCentralBundleAsync(
            CancellationToken cancellationToken) =>
        BusinessCentralProviderFactory.Create(
            await _configurationResolver.GetBusinessCentralConnectionAsync(
                cancellationToken));
}
