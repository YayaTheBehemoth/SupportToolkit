using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// An opt-in review limited to tenants the shared monitoring scope
/// selects.
///
/// Separate from BackupAggregatorWorkflow on purpose: the existing
/// all-tenant review and the ticket workflow do not depend on, call
/// or change with this class. A scoped ticket is built from a scoped
/// result only when the review is complete.
///
/// The feature is available only when the active profile selects a
/// Business Central connection. Fixture mode runs entirely on synthetic data.
/// </summary>
public sealed class BackupAggregatorScopedWorkflow
{
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    private readonly IMonitoringIdentitySource? _identitySource;

    private readonly ICustomerMappingRepository? _mappingRepository;

    private readonly Func<CancellationToken,
        Task<ISmartSubscriptionEvidenceSource>> _evidenceSourceFactory;

    private readonly Func<CancellationToken,
        Task<ICustomerNameSource>> _nameSourceFactory;

    private readonly Func<CancellationToken,
        Task<ScopedInventoryRuntime>> _inventoryRuntimeFactory;

    // One Business Central bundle per workflow, so the evidence and
    // the names share a single sign-in.
    private Task<BusinessCentralProviderBundle>? _businessCentralBundle;

    public BackupAggregatorScopedWorkflow(
        SupportToolkitConfigurationResolver configurationResolver,
        IMonitoringIdentitySource? identitySource = null,
        Func<CancellationToken,
            Task<ISmartSubscriptionEvidenceSource>>?
            evidenceSourceFactory = null,
        Func<CancellationToken,
            Task<ScopedInventoryRuntime>>?
            inventoryRuntimeFactory = null,
        ICustomerMappingRepository? mappingRepository = null,
        Func<CancellationToken,
            Task<ICustomerNameSource>>?
            nameSourceFactory = null)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver));

        _mappingRepository =
            mappingRepository;

        /*
         * An explicitly supplied identity source decides identities
         * as before (fixtures, tests). Otherwise they are resolved
         * automatically each run from Business Central customers and
         * Acronis tenants, with any stored mappings taking precedence.
         */
        _identitySource =
            identitySource;

        _evidenceSourceFactory =
            evidenceSourceFactory
            ?? CreateBusinessCentralEvidenceSourceAsync;

        _nameSourceFactory =
            nameSourceFactory
            ?? CreateBusinessCentralNameSourceAsync;

        _inventoryRuntimeFactory =
            inventoryRuntimeFactory
            ?? CreateAcronisRuntimeAsync;
    }

    /// <summary>
    /// Local check only: whether the active profile selects a
    /// Business Central connection.
    /// </summary>
    public Task<bool> IsEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        return _configurationResolver
            .IsMonitoringScopeEnabledAsync(cancellationToken);
    }

    /// <summary>
    /// Scoped review according to the BackupAggregator runtime mode.
    ///
    /// Fixture mode uses synthetic subscriptions, tenants and
    /// inventory and makes no external request.
    ///
    /// Production order is deliberate: local configuration is read
    /// before any sign-in or API request, so a missing setting fails
    /// with no external activity. Identities are then resolved from
    /// the evidence and the tenants, never assumed.
    /// </summary>
    public async Task<ScopedBackupInventoryReviewResult> ReviewAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var mode =
            await _configurationResolver.GetModuleModeAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        if (mode == SupportToolkitMode.Fixture)
        {
            var inventory =
                new HealthyFixtureInventoryProvider(asOfDate);

            var fixtureMappings =
                await CreateFixtureMappingRepositoryAsync(
                    cancellationToken);

            return await ExecuteAsync(
                [SmartSubscriptionFixtureFactory
                    .FixtureMonitoringItemNumber],
                new ConfirmedMappingIdentitySource(fixtureMappings),
                _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                    new FixtureSmartSubscriptionEvidenceSource()),
                null,
                _ => Task.FromResult(
                    new ScopedInventoryRuntime(
                        new BackupInventoryReviewService(
                            new AcronisTenantResolver(
                                new FixtureCustomerTenantProvider()),
                            inventory,
                            inventory,
                            new BackupInventoryNormalizer()),
                        null)),
                asOfDate,
                cancellationToken,
                mappingRepository: fixtureMappings);
        }

        var itemNumbers =
            await _configurationResolver
                .GetMonitoringItemNumbersAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        var excludedTenantIds =
            await _configurationResolver
                .GetExcludedTenantIdsAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        var serviceItemNumbers =
            await _configurationResolver
                .GetServiceItemNumbersAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken);

        return await ExecuteAsync(
            itemNumbers,
            _identitySource,
            _evidenceSourceFactory,
            _nameSourceFactory,
            _inventoryRuntimeFactory,
            asOfDate,
            cancellationToken,
            excludedTenantIds,
            _mappingRepository,
            serviceItemNumbers);
    }

    /// <summary>
    /// Synthetic Confirmed mappings matching the fixture identities.
    /// </summary>
    private static async Task<ICustomerMappingRepository>
        CreateFixtureMappingRepositoryAsync(
            CancellationToken cancellationToken)
    {
        var repository =
            new InMemoryCustomerMappingRepository();

        foreach (var identity in await new FixtureMonitoringIdentitySource()
            .GetIdentitiesAsync(cancellationToken))
        {
            await repository.AddAsync(
                new CustomerMappingRecord(
                    Guid.NewGuid(),
                    identity.AcronisTenantId,
                    identity.BusinessCentralCustomerNumber,
                    CustomerMappingState.Confirmed,
                    CustomerMatchMethod.Manual),
                cancellationToken);
        }

        return repository;
    }

    private static async Task<ScopedBackupInventoryReviewResult>
        ExecuteAsync(
            IReadOnlyCollection<string> itemNumbers,
            IMonitoringIdentitySource? identitySource,
            Func<CancellationToken,
                Task<ISmartSubscriptionEvidenceSource>>
                evidenceSourceFactory,
            Func<CancellationToken, Task<ICustomerNameSource>>?
                nameSourceFactory,
            Func<CancellationToken, Task<ScopedInventoryRuntime>>
                inventoryRuntimeFactory,
            DateOnly asOfDate,
            CancellationToken cancellationToken,
            IReadOnlySet<string>? excludedTenantIds = null,
            ICustomerMappingRepository? mappingRepository = null,
            IReadOnlyCollection<string>? serviceItemNumbers = null)
    {
        // An explicit identity source decides before any external read.
        var explicitIdentities =
            identitySource is null
                ? null
                : await identitySource.GetIdentitiesAsync(
                    cancellationToken);

        // The shared scope service decides who is entitled for this
        // module's items; what that narrows is decided below.
        var scope =
            await new MonitoringScopeService(
                await evidenceSourceFactory(cancellationToken))
                .ResolveAsync(
                    itemNumbers,
                    asOfDate,
                    cancellationToken,
                    serviceItemNumbers);

        var runtime =
            await inventoryRuntimeFactory(cancellationToken);

        using (runtime.Lifetime)
        {
            // Every tenant, not only customers: recognizing the
            // operator's own company needs its partner tenant.
            var tenants =
                await runtime.ReviewService
                    .GetAllTenantsAsync(cancellationToken);

            IReadOnlyList<ResolvedMonitoringCustomerIdentity> identities;
            ICustomerMappingRepository? reconciliationMappings;
            AutomaticMappingSummary? automatic = null;
            string? nameFailure = null;

            if (explicitIdentities is not null)
            {
                identities = explicitIdentities;
                reconciliationMappings = mappingRepository;
            }
            else
            {
                var resolved =
                    await ResolveIdentitiesAsync(
                        scope,
                        tenants,
                        nameSourceFactory,
                        mappingRepository,
                        excludedTenantIds,
                        cancellationToken);

                identities = resolved.Identities;
                reconciliationMappings = resolved.Mappings;
                automatic = resolved.Summary;
                nameFailure = resolved.Summary.NameFailure;
            }

            var selection =
                new MonitoringTenantSelector()
                    .Select(
                        tenants,
                        scope.Observations,
                        identities,
                        excludedTenantIds);

            if (nameFailure is not null)
            {
                selection =
                    selection with
                    {
                        Notices =
                        [
                            .. selection.Notices,
                            "Business Central customer names could not " +
                            "be read, so tenants could not be matched " +
                            "by name."
                        ]
                    };
            }

            var result =
                await runtime.ReviewService
                    .ReviewSelectedTenantsAsync(
                        selection,
                        cancellationToken);

            /*
             * Reconciliation needs the mappings themselves, not just
             * the resolved identities. No monitored-tenant evidence
             * source exists yet, which the report states.
             */
            var reconciliation =
                reconciliationMappings is null
                    ? null
                    : new ReconciliationEngine()
                        .Reconcile(
                            await reconciliationMappings.GetAllAsync(
                                cancellationToken),
                            scope.Evidence.Headers,
                            scope.Evidence.Lines,
                            AcronisTenantObservations.FromCustomerTenants(
                                tenants),
                            asOfDate,
                            SubscriptionItemNumbers.ToSet(itemNumbers),
                            // Excluded tenants, and the operator's own
                            // tenant, which has no customer to match.
                            selection.ExcludedTenants
                                .Select(excluded => excluded.Tenant.Id)
                                .Concat(
                                    selection.Tenants
                                        .Where(finding => finding.IsOperatorTenant)
                                        .Select(finding => finding.Tenant.Id))
                                .ToHashSet(StringComparer.OrdinalIgnoreCase),
                            monitoredTenantIds: null);

            return result with
            {
                ScopeReport =
                    MonitoringScopeReportBuilder.Build(
                        asOfDate,
                        itemNumbers.ToArray(),
                        scope.Observations,
                        selection,
                        reconciliation,
                        monitoredEvidenceSupplied: false,
                        tenants,
                        scope.ItemHolderCustomerNumbers)
                    with
                    {
                        AutomaticMappings = automatic
                    }
            };
        }
    }

    /// <summary>
    /// Resolves Business Central customers to Acronis tenants for this
    /// run. Stored mappings are copied into a run-local repository, so
    /// human decisions win and automatic decisions are never written
    /// back.
    /// </summary>
    private static async Task<AutomaticResolution> ResolveIdentitiesAsync(
        MonitoringScopeResult scope,
        IReadOnlyList<TenantDto> tenants,
        Func<CancellationToken, Task<ICustomerNameSource>>? nameSourceFactory,
        ICustomerMappingRepository? storedMappings,
        IReadOnlySet<string>? excludedTenantIds,
        CancellationToken cancellationToken)
    {
        // Every customer of the service is matched, entitled or not:
        // a tenant of a customer with the backup but no monitoring
        // add-on is then decided (not entitled) rather than unresolved.
        // Customers with any other subscription are matched too, so a
        // tenant whose customer holds neither item is recognized as a
        // leak candidate instead of staying unmatched. The evidence
        // already holds them; no extra read is made. Only when service
        // items are configured: without them a customer with the
        // backup cannot be told from one without, so tenants of other
        // customers stay unmatched and the scope stays incomplete.
        var customerNumbers =
            scope.ServiceCustomerNumbers
                .Concat(
                    scope.ItemHolderCustomerNumbers is null
                        ? []
                        : scope.AllSubscriptionCustomerNumbers)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(number => number, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var names =
            nameSourceFactory is null
                ? new ScopedCustomerParties(
                    customerNumbers
                        .Select(number => new CustomerParty(number))
                        .ToArray(),
                    customerNumbers.Length,
                    0,
                    "No customer name source is configured.")
                : await (await nameSourceFactory(cancellationToken))
                    .GetCustomerPartiesAsync(
                        customerNumbers.ToArray(),
                        cancellationToken);

        var excluded =
            excludedTenantIds
            ?? new HashSet<string>();

        var acronisParties =
            AcronisCustomerParties.ForResolution(tenants)
                .Parties
                .Where(
                    party => !excluded.Contains(party.Id.Trim()))
                .ToArray();

        var runRepository =
            new InMemoryCustomerMappingRepository();

        if (storedMappings is not null)
        {
            foreach (var mapping in await storedMappings
                .GetAllAsync(cancellationToken))
            {
                await runRepository.AddAsync(
                    mapping,
                    cancellationToken);
            }
        }

        // Two passes, so widening the pool can only add matches. The
        // first pass sees exactly the customers it always saw (those
        // holding an entitling or service item); a tenant it accepts
        // is never reconsidered. The second pass offers the remaining
        // tenants the other subscription customers too. Without the
        // split, a similarly named customer outside the service could
        // turn an earlier unique match into an ambiguous one.
        var itemHolders =
            scope.ServiceCustomerNumbers
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var itemHolderParties =
            names.Parties
                .Where(party => itemHolders.Contains(party.Id.Trim()))
                .ToArray();

        var resolver =
            new AutoCustomerMappingResolver();

        var resolution =
            await resolver.ResolveAsync(
                acronisParties,
                itemHolderParties,
                runRepository,
                cancellationToken);

        if (itemHolderParties.Length < names.Parties.Count)
        {
            var wider =
                await resolver.ResolveAsync(
                    acronisParties,
                    names.Parties,
                    runRepository,
                    cancellationToken);

            resolution =
                wider with
                {
                    Accepted =
                        [.. resolution.Accepted, .. wider.Accepted],
                    PendingReview =
                        [.. resolution.PendingReview, .. wider.PendingReview]
                };
        }

        var identities =
            await new ConfirmedMappingIdentitySource(runRepository)
                .GetIdentitiesAsync(cancellationToken);

        var nameOf =
            names.Parties.ToDictionary(
                party => party.Id.Trim(),
                party => party.Name,
                StringComparer.OrdinalIgnoreCase);

        // A gap is an entitled customer with no tenant. Customers that
        // only hold the service item are expected to have none.
        var gaps =
            resolution.UnmatchedBcCustomerNumbers
                .Where(
                    number => scope.Customers.ContainsKey(number.Trim())
                        && scope.StateOf(number)
                            != CustomerEntitlementState.Inactive)
                .Select(
                    number => new ScopeCustomerGap(
                        number,
                        nameOf.GetValueOrDefault(number.Trim()),
                        scope.StateOf(number)))
                .ToArray();

        var summary =
            new AutomaticMappingSummary(
                resolution.Accepted
                    .GroupBy(mapping => mapping.MatchMethod)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Count()),
                resolution.PendingReview.Count,
                resolution.Exceptions.Count,
                gaps,
                names.CustomersWithoutName,
                names.CustomersWithConflictingNames,
                names.Failure)
            {
                EntitledWithoutServiceItem =
                    scope.EntitledWithoutServiceItem
                        .Select(
                            number => new ScopeCustomerGap(
                                number,
                                nameOf.GetValueOrDefault(number.Trim()),
                                scope.StateOf(number)))
                        .ToArray()
            };

        return new AutomaticResolution(
            identities,
            runRepository,
            summary);
    }

    private sealed record AutomaticResolution(
        IReadOnlyList<ResolvedMonitoringCustomerIdentity> Identities,
        ICustomerMappingRepository Mappings,
        AutomaticMappingSummary Summary);

    private Task<BusinessCentralProviderBundle>
        GetBusinessCentralBundleAsync(
            CancellationToken cancellationToken)
    {
        return _businessCentralBundle ??=
            BusinessCentralProviderFactory.CreateAsync(
                _configurationResolver,
                cancellationToken);
    }

    private async Task<ISmartSubscriptionEvidenceSource>
        CreateBusinessCentralEvidenceSourceAsync(
            CancellationToken cancellationToken)
    {
        var bundle =
            await GetBusinessCentralBundleAsync(cancellationToken);

        return new BusinessCentralSmartSubscriptionEvidenceSource(
            bundle.Provider,
            BusinessCentralSourceContext.From(bundle.Options));
    }

    private async Task<ICustomerNameSource>
        CreateBusinessCentralNameSourceAsync(
            CancellationToken cancellationToken)
    {
        var bundle =
            await GetBusinessCentralBundleAsync(cancellationToken);

        return new BusinessCentralCustomerNameSource(
            bundle.Provider);
    }

    private async Task<ScopedInventoryRuntime>
        CreateAcronisRuntimeAsync(
            CancellationToken cancellationToken)
    {
        var connection =
            await _configurationResolver
                .GetAcronisConnectionAsync(cancellationToken);

        var wait =
            await InProgressWaitSetup.LoadAsync(
                _configurationResolver,
                BackupAggregatorModule.ModuleCommand,
                cancellationToken);

        var session =
            BackupAggregatorProductionSession.Create(
                AcronisOptions.Create(
                    connection.DatacenterUrl,
                    connection.ClientId,
                    connection.ClientSecret),
                OperationalLogger.FromEnvironment(),
                wait,
                InProgressWaitSetup.WriteProgress);

        return new ScopedInventoryRuntime(
            session.ReviewService,
            session);
    }
}

/// <summary>
/// The inventory review service and the lifetime that owns its
/// transport.
/// </summary>
public sealed record ScopedInventoryRuntime(
    BackupInventoryReviewService ReviewService,
    IDisposable? Lifetime);
