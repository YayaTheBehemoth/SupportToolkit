using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

/// <summary>
/// The production scoped review with identities resolved
/// automatically: Business Central entitlement decides which tenants
/// are reviewed, and everything else stays out.
/// </summary>
public class BackupAggregatorAutomaticScopeTests
{
    private const string Item = "ITEM-1";

    private static readonly DateOnly Today = new(2026, 10, 15);

    private const string Northwind = "11111111-1111-1111-1111-111111111111";
    private const string Contoso = "22222222-2222-2222-2222-222222222222";
    private const string Stranger = "33333333-3333-3333-3333-333333333333";
    private const string Suspended = "44444444-4444-4444-4444-444444444444";
    private const string OwnPartner = "55555555-5555-5555-5555-555555555555";
    private const string OwnCustomer = "66666666-6666-6666-6666-666666666666";

    [Fact]
    public async Task EntitledCustomersAndTheOperatorsOwnTenant_AreReviewed()
    {
        var run = await RunAsync();

        // The operator's own tenant is billed to no one and is always
        // included beside the entitled customers. Its customer-kind
        // tenant is the one reviewed; the partner tenant is not.
        Assert.Equal(
            [Northwind, Contoso, OwnCustomer],
            run.Inventory.DeviceRequests.Order());

        Assert.Equal(
            [Northwind, Contoso, OwnCustomer],
            run.Result.Selection.InventoryCandidates
                .Select(tenant => tenant.Id)
                .Order());

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        Assert.Single(
            report.InScope,
            tenant => tenant.IsOperatorTenant
                && tenant.TenantId == OwnCustomer);
    }

    [Fact]
    public async Task UnmappedTenantsStayOut_AndThePartnerTenantIsNeverReviewed()
    {
        var run = await RunAsync();

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        Assert.DoesNotContain(Stranger, run.Inventory.DeviceRequests);
        Assert.DoesNotContain(OwnPartner, run.Inventory.DeviceRequests);

        Assert.Contains(
            report.UnscopedTenants,
            tenant => tenant.TenantId == Stranger);

        // The own tenant is in scope, never unscoped or excluded.
        Assert.DoesNotContain(
            report.UnscopedTenants,
            tenant => tenant.TenantId == OwnCustomer);

        Assert.DoesNotContain(
            report.Excluded,
            tenant => tenant.TenantId == OwnCustomer);
    }

    [Fact]
    public async Task ReportCountsAutomaticMappings_AndListsOnlyRealGaps()
    {
        var run = await RunAsync();

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        var summary = Assert.IsType<AutomaticMappingSummary>(
            report.AutomaticMappings);

        // BC-3 is active with no tenant. BC-4 is suspended with no
        // tenant and is ignored.
        Assert.Equal(
            ["BC-3"],
            summary.CustomersWithoutTenant.Select(gap => gap.CustomerNumber));

        Assert.Equal("Fabrikam", summary.CustomersWithoutTenant[0].CustomerName);
        Assert.True(summary.Accepted >= 2);
        Assert.Null(summary.NameFailure);
        Assert.False(run.Result.IsScopeComplete);
    }

    [Fact]
    public async Task SuspendedCustomerWithATenant_IsNotReviewed_ButReconciled()
    {
        var run = await RunAsync();

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        Assert.DoesNotContain(Suspended, run.Inventory.DeviceRequests);

        Assert.Contains(
            report.Excluded,
            tenant => tenant.TenantId == Suspended
                && tenant.Reason == ScopeExclusionReason.NotEntitled);

        Assert.Contains(
            report.Discrepancies,
            item => item.Discrepancy.Key.Type
                == DiscrepancyType.TenantActiveSubscriptionInactive);
    }

    [Fact]
    public async Task StoredMapping_BeatsAnAutomaticOne_AndIsNeverWrittenTo()
    {
        var stored =
            new InMemoryCustomerMappingRepository();

        await stored.AddAsync(
            new CustomerMappingRecord(
                Guid.NewGuid(),
                Northwind,
                "BC-9",
                CustomerMappingState.Confirmed,
                CustomerMatchMethod.Manual,
                ConfirmedAt: DateTimeOffset.UtcNow,
                ConfirmedBy: "tester"));

        var run = await RunAsync(storedMappings: stored);

        Assert.DoesNotContain(Northwind, run.Inventory.DeviceRequests);
        Assert.Contains(Contoso, run.Inventory.DeviceRequests);

        Assert.Single(await stored.GetAllAsync());
    }

    [Fact]
    public async Task FailedNameRead_DegradesMatching_AndSaysSo()
    {
        var run = await RunAsync(nameFailure: "Business Central returned an unexpected response (HTTP 400).");

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        // No customer can be matched without names; the operator's
        // own tenant needs no match and is still reviewed.
        Assert.Equal([OwnCustomer], run.Inventory.DeviceRequests);
        Assert.Equal(
            OwnCustomer,
            Assert.Single(run.Result.Selection.InventoryCandidates).Id);
        Assert.False(run.Result.IsScopeComplete);

        Assert.Contains(
            report.Notices,
            notice => notice.Contains("could not be read"));

        Assert.NotNull(report.AutomaticMappings!.NameFailure);
        Assert.Equal(0, report.AutomaticMappings.Accepted);
    }

    [Fact]
    public async Task ExplicitIdentitySource_SkipsAutomaticResolution()
    {
        var nameCalls = 0;

        var inventory = new HealthyFixtureInventoryProvider(Today);

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(),
                new FixtureMonitoringIdentitySource(),
                _ => Task.FromResult<ISmartSubscriptionEvidenceSource>(
                    new FixtureSmartSubscriptionEvidenceSource()),
                _ => Task.FromResult(
                    new ScopedInventoryRuntime(
                        new BackupInventoryReviewService(
                            new AcronisTenantResolver(
                                new FixtureCustomerTenantProvider()),
                            inventory,
                            inventory,
                            new BackupInventoryNormalizer()),
                        null)),
                nameSourceFactory: _ =>
                {
                    nameCalls++;
                    throw new InvalidOperationException("must not run");
                });

        var result = await workflow.ReviewAsync(Today);

        Assert.Equal(0, nameCalls);
        Assert.Null(result.ScopeReport!.AutomaticMappings);
    }

    private static async Task<Run> RunAsync(
        ICustomerMappingRepository? storedMappings = null,
        string? nameFailure = null)
    {
        var inventory = new HealthyFixtureInventoryProvider(Today);

        IReadOnlyList<TenantDto> tenants =
        [
            Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
            Tenant(OwnCustomer, "Synthetic Operator", "customer"),
            Tenant(Northwind, "Northwind Traders", "customer"),
            Tenant(Contoso, "Contoso", "customer"),
            Tenant(Stranger, "Unrelated Tenant", "customer"),
            Tenant(Suspended, "Adventure Works", "customer")
        ];

        IReadOnlyList<SmartSubscriptionHeader> headers =
        [
            Header("SUB-1", "BC-1", "Active"),
            Header("SUB-2", "BC-2", "Active"),
            Header("SUB-3", "BC-3", "Active"),
            Header("SUB-4", "BC-4", "Suspended"),
            Header("SUB-5", "BC-5", "Suspended")
        ];

        IReadOnlyList<SmartSubscriptionLine> lines =
            headers
                .Select(
                    header => new SmartSubscriptionLine(
                        header.SubscriptionNumber,
                        Item,
                        "Item",
                        false))
                .ToArray();

        var names =
            nameFailure is null
                ? new ScopedCustomerParties(
                    [
                        new("BC-1", "Northwind Traders ApS"),
                        new("BC-2", "Contoso Vaerksted A/S"),
                        new("BC-3", "Fabrikam"),
                        new("BC-4", "Gone Customer"),
                        new("BC-5", "Adventure Works")
                    ],
                    0,
                    0)
                : new ScopedCustomerParties(
                    new[] { "BC-1", "BC-2", "BC-3", "BC-4", "BC-5" }
                        .Select(number => new CustomerParty(number))
                        .ToArray(),
                    5,
                    0,
                    nameFailure);

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(),
                evidenceSourceFactory: _ =>
                    Task.FromResult<ISmartSubscriptionEvidenceSource>(
                        new FakeEvidence(headers, lines)),
                inventoryRuntimeFactory: _ =>
                    Task.FromResult(
                        new ScopedInventoryRuntime(
                            new BackupInventoryReviewService(
                                new AcronisTenantResolver(
                                    new FakeTenants(tenants)),
                                inventory,
                                inventory,
                                new BackupInventoryNormalizer()),
                            null)),
                mappingRepository: storedMappings,
                nameSourceFactory: _ =>
                    Task.FromResult<ICustomerNameSource>(
                        new FakeNames(names)));

        return new Run(
            await workflow.ReviewAsync(Today),
            inventory);
    }

    private const string Alpine = "77777777-7777-7777-7777-777777777777";

    private static readonly string[] ServiceItems =
        ["EXAMPLE-BACKUP", "EXAMPLE-BACKUP-2", "EXAMPLE BACKUP PLUS"];

    [Fact]
    public async Task ServiceItemCustomers_AreDecided_SoTheReviewCanBeComplete()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                Sub("SUB-2", "BC-2", "EXAMPLE-BACKUP"),
                Sub("SUB-3", "BC-3", "EXAMPLE BACKUP PLUS")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Contoso, "Contoso", "customer"),
                Tenant(Alpine, "Alpine Consulting", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S"),
                new("BC-3", "Alpine Consulting ApS")
            ],
            ServiceItems);

        // Only the entitled customer and the operator are reviewed.
        Assert.Equal(
            [Northwind, OwnCustomer],
            run.Inventory.DeviceRequests.Order());

        // Backup-only customers are matched and decided, not unresolved.
        Assert.Equal(
            2,
            run.Result.Selection.NotEntitledCount);

        Assert.Equal(0, run.Result.Selection.UnclassifiedCount);

        Assert.True(run.Result.IsScopeComplete);
        Assert.True(run.Result.IsInventoryComplete);
        Assert.True(run.Result.IsOverallComplete);
    }

    [Fact]
    public async Task TenantWithoutAnyServiceCustomer_StillKeepsTheScopeIncomplete()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Stranger, "Unrelated Tenant", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            ServiceItems);

        Assert.Equal(1, run.Result.Selection.UnclassifiedCount);
        Assert.False(run.Result.IsScopeComplete);
    }

    [Fact]
    public async Task WithoutServiceItems_BackupOnlyCustomersStayUnmatched()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                Sub("SUB-2", "BC-2", "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Contoso, "Contoso", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S")
            ],
            serviceItems: null);

        Assert.Equal(1, run.Result.Selection.UnclassifiedCount);
        Assert.False(run.Result.IsScopeComplete);
    }

    [Fact]
    public async Task EntitledCustomerWithoutAServiceItem_IsFlaggedNotCountedAsAGap()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item),
                Sub("SUB-2", "BC-2", "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S")
            ],
            ServiceItems);

        var summary =
            Assert.IsType<MonitoringScopeReport>(run.Result.ScopeReport)
                .AutomaticMappings!;

        Assert.Equal(
            ["BC-1"],
            summary.EntitledWithoutServiceItem
                .Select(customer => customer.CustomerNumber));

        // BC-2 only holds the service item and has no tenant: that is
        // expected, not a gap.
        Assert.Empty(summary.CustomersWithoutTenant);
    }

    [Fact]
    public async Task CompleteScopedReview_ProducesATicketDraftThatSaysItsScope()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                Sub("SUB-2", "BC-2", "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Contoso, "Contoso", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S")
            ],
            ServiceItems);

        Assert.Empty(run.Result.TicketBlockers);

        var draft =
            new BackupAggregatorWorkflow(Resolver(ServiceItems))
                .CreateScopedTicketDraft(run.Result);

        Assert.Contains("SCOPE", draft.Body);
        Assert.Contains(
            "Monitored tenants only: 1 entitled customer tenant(s) by " +
            "Business Central entitlement, plus the operator's own tenant.",
            draft.Body);
        Assert.Contains("Tenants reviewed: 2/2", draft.Body);
    }

    [Fact]
    public async Task UnmatchedTenant_ProducesATicketWithALoudIncompleteScopeBanner()
    {
        var run = await RunScenarioAsync(
            [Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP")],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Stranger, "Unrelated Tenant", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            ServiceItems);

        Assert.False(run.Result.IsScopeComplete);
        Assert.False(run.Result.IsOverallComplete);
        Assert.Empty(run.Result.TicketBlockers);

        var draft =
            new BackupAggregatorWorkflow(Resolver(ServiceItems))
                .CreateScopedTicketDraft(run.Result);

        Assert.StartsWith("[INCOMPLETE SCOPE] ", draft.Subject);
        Assert.Contains(
            "!!! INCOMPLETE SCOPE: 1 tenant(s) could not be matched " +
            "to a Business Central customer and were NOT reviewed.",
            draft.Body);
        Assert.Contains("Unmatched tenants (1)", draft.Body);
        Assert.Contains("- Unrelated Tenant", draft.Body);

        // The banner comes before everything else.
        Assert.True(
            draft.Body.IndexOf("!!! INCOMPLETE SCOPE", StringComparison.Ordinal)
                < draft.Body.IndexOf("STRAY TENANT CHECK", StringComparison.Ordinal));

        Assert.StartsWith(
            "SUPPORTTOOLKIT // BACKUP REVIEW",
            draft.Body);

        Assert.True(
            draft.Body.IndexOf("!!! INCOMPLETE SCOPE", StringComparison.Ordinal)
                < draft.Body.IndexOf("SUMMARY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteScope_HasNoBannerAndNoSubjectPrefix()
    {
        var run = await RunScenarioAsync(
            [Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP")],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            ServiceItems);

        var draft =
            new BackupAggregatorWorkflow(Resolver(ServiceItems))
                .CreateScopedTicketDraft(run.Result);

        Assert.DoesNotContain("INCOMPLETE SCOPE", draft.Subject);
        Assert.DoesNotContain("INCOMPLETE SCOPE", draft.Body);
        Assert.Null(run.Result.TicketScopeWarning);
    }

    [Fact]
    public async Task AnEntitledCustomerWithNoTenant_StillBlocksTheTicket()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                Sub("SUB-2", "BC-2", Item, "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S")
            ],
            ServiceItems);

        Assert.NotEmpty(run.Result.TicketBlockers);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => new BackupAggregatorWorkflow(Resolver(ServiceItems))
                    .CreateScopedTicketDraft(run.Result));

        Assert.Contains("not complete", exception.Message);
    }

    [Fact]
    public async Task ScopedTicketDraft_IsDistinctFromAFullReviewTicket()
    {
        var run = await RunScenarioAsync(
            [Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP")],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            ServiceItems);

        var workflow =
            new BackupAggregatorWorkflow(Resolver(ServiceItems));

        var scoped =
            workflow.CreateScopedTicketDraft(run.Result);

        var full =
            workflow.CreateTicketDraft(run.Result.Inventory);

        Assert.Contains("SCOPE", scoped.Body);
        Assert.DoesNotContain("SCOPE", full.Body);
    }

    private const string Fabrikam = "77777777-7777-7777-7777-777777777777";

    [Fact]
    public async Task TenantOfACustomerWithNeitherItem_IsALeakCandidateInReportConsoleAndTicket()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                // Backup but no monitoring: a legitimate exclusion.
                Sub("SUB-2", "BC-2", "EXAMPLE-BACKUP"),
                // Subscribes to something unrelated: no backup at all.
                Sub("SUB-3", "BC-3", "OTHER-SERVICE")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Contoso, "Contoso", "customer"),
                Tenant(Fabrikam, "Fabrikam Industries", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S"),
                new("BC-3", "Fabrikam Industries A/S")
            ],
            ServiceItems);

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        var check = Assert.IsType<StrayTenantCheck>(report.StrayTenants);

        Assert.True(check.LeakCheckAvailable);

        var leak = Assert.Single(check.LeakCandidates);
        Assert.Equal("Fabrikam Industries", leak.TenantName);
        Assert.Equal("BC-3", leak.CustomerNumber);
        Assert.Empty(check.Unmatched);

        // The backup-only customer is excluded but is not a leak.
        Assert.Contains(
            report.Excluded,
            tenant => tenant.TenantName == "Contoso");

        // Everything is decided, so the scope is complete and a ticket
        // may be drafted; the stray check is informational.
        Assert.Empty(run.Result.TicketBlockers);

        using var writer = new StringWriter();

        new SupportToolkit.Modules.BackupAggregator.Reporting
            .ConsoleMonitoringScopeReporter()
            .Write(writer, report, detailed: false);

        var console = writer.ToString();

        Assert.Contains("STRAY TENANT CHECK (1)", console);
        Assert.Contains("Fabrikam Industries | BC-3", console);
        Assert.DoesNotContain("Contoso |", console.Split("STRAY TENANT CHECK")[1]);

        var draft =
            new BackupAggregatorWorkflow(Resolver(ServiceItems))
                .CreateScopedTicketDraft(run.Result);

        Assert.Contains("STRAY TENANT CHECK", draft.Body);
        Assert.Contains("Leak candidates (1)", draft.Body);
        Assert.Contains("- Fabrikam Industries (customer BC-3)", draft.Body);
        Assert.Contains("Unmatched tenants: none.", draft.Body);
        Assert.DoesNotContain("- Contoso (customer", draft.Body);
    }

    [Fact]
    public async Task ScopeSection_ListsTheInScopeTenantsSortedWithTheOperatorMarked()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                Sub("SUB-2", "BC-2", Item, "EXAMPLE-BACKUP")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Contoso, "Contoso", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-2", "Contoso Vaerksted A/S")
            ],
            ServiceItems);

        Assert.Equal(
            [
                "Contoso",
                "Northwind Traders",
                "Synthetic Operator (operator's own tenant)"
            ],
            run.Result.TicketScopeTenantLines);

        var body =
            new BackupAggregatorWorkflow(Resolver(ServiceItems))
                .CreateScopedTicketDraft(run.Result)
                .Body;

        var scope =
            body.Split("SCOPE")[1];

        Assert.True(
            scope.IndexOf("- Contoso", StringComparison.Ordinal)
                < scope.IndexOf("- Northwind Traders", StringComparison.Ordinal));

        Assert.Contains(
            "- Synthetic Operator (operator's own tenant)",
            body);

        Assert.Contains("2 entitled customer tenant(s)", body);
    }

    [Fact]
    public async Task ASimilarlyNamedCustomerOutsideTheService_DoesNotBreakAnEarlierUniqueMatch()
    {
        var run = await RunScenarioAsync(
            [
                Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP"),
                // Unrelated subscription, near-identical name.
                Sub("SUB-2", "BC-9", "OTHER-SERVICE")
            ],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [
                new("BC-1", "Northwind Traders ApS"),
                new("BC-9", "Northwind Traders Holding ApS")
            ],
            ServiceItems);

        var report = Assert.IsType<MonitoringScopeReport>(
            run.Result.ScopeReport);

        Assert.Contains(
            report.InScope,
            tenant => tenant.TenantId == Northwind
                && tenant.CustomerNumber == "BC-1");

        Assert.Empty(run.Result.TicketBlockers);
    }

    [Fact]
    public async Task WithoutServiceItems_TheLeakCheckIsUnavailableNeverClean()
    {
        var run = await RunScenarioAsync(
            [Sub("SUB-1", "BC-1", Item)],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            serviceItems: null);

        var check = Assert.IsType<StrayTenantCheck>(
            Assert.IsType<MonitoringScopeReport>(run.Result.ScopeReport)
                .StrayTenants);

        Assert.False(check.LeakCheckAvailable);
        Assert.Empty(check.LeakCandidates);

        var body =
            new BackupAggregatorWorkflow(Resolver(null))
                .CreateScopedTicketDraft(run.Result)
                .Body;

        Assert.Contains("Leak check unavailable", body);
        Assert.DoesNotContain("Leak candidates: none.", body);
    }

    [Fact]
    public async Task UnmatchedTenant_IsListedForReviewInTheStrayCheck()
    {
        var run = await RunScenarioAsync(
            [Sub("SUB-1", "BC-1", Item, "EXAMPLE-BACKUP")],
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(Northwind, "Northwind Traders", "customer"),
                Tenant(Stranger, "Unrelated Tenant", "customer")
            ],
            [new("BC-1", "Northwind Traders ApS")],
            ServiceItems);

        var check = Assert.IsType<StrayTenantCheck>(
            Assert.IsType<MonitoringScopeReport>(run.Result.ScopeReport)
                .StrayTenants);

        var unmatched = Assert.Single(check.Unmatched);
        Assert.Equal("Unrelated Tenant", unmatched.TenantName);
        Assert.False(run.Result.IsScopeComplete);
    }

    private static (string Subscription, string Customer, string[] Items) Sub(
        string subscription,
        string customer,
        params string[] items) =>
        (subscription, customer, items);

    private static async Task<Run> RunScenarioAsync(
        (string Subscription, string Customer, string[] Items)[] subscriptions,
        IReadOnlyList<TenantDto> tenants,
        IReadOnlyList<CustomerParty> names,
        string[]? serviceItems)
    {
        var inventory = new HealthyFixtureInventoryProvider(Today);

        var headers =
            subscriptions
                .Select(
                    subscription => Header(
                        subscription.Subscription,
                        subscription.Customer,
                        "Active"))
                .ToArray();

        var lines =
            subscriptions
                .SelectMany(
                    subscription => subscription.Items.Select(
                        item => new SmartSubscriptionLine(
                            subscription.Subscription,
                            item,
                            "Item",
                            false)))
                .ToArray();

        var workflow =
            new BackupAggregatorScopedWorkflow(
                Resolver(serviceItems),
                evidenceSourceFactory: _ =>
                    Task.FromResult<ISmartSubscriptionEvidenceSource>(
                        new FakeEvidence(headers, lines)),
                inventoryRuntimeFactory: _ =>
                    Task.FromResult(
                        new ScopedInventoryRuntime(
                            new BackupInventoryReviewService(
                                new AcronisTenantResolver(
                                    new FakeTenants(tenants)),
                                inventory,
                                inventory,
                                new BackupInventoryNormalizer()),
                            null)),
                nameSourceFactory: _ =>
                    Task.FromResult<ICustomerNameSource>(
                        new FakeNames(
                            new ScopedCustomerParties(names, 0, 0))));

        return new Run(
            await workflow.ReviewAsync(Today),
            inventory);
    }

    private sealed record Run(
        ScopedBackupInventoryReviewResult Result,
        HealthyFixtureInventoryProvider Inventory);

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status)
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            null,
            " ");
    }

    private static TenantDto Tenant(
        string id,
        string name,
        string kind)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind,
            Enabled = true
        };
    }

    private static SupportToolkitConfigurationResolver Resolver(
        string[]? serviceItems = null)
    {
        var configuration =
            new SupportToolkitConfiguration
            {
                ActiveProfile = "work",

                Profiles =
                {
                    ["work"] =
                        new SupportToolkitProfile
                        {
                            BusinessCentralConnection = "bc-work",

                            Modules =
                            {
                                ["backup-aggregator"] =
                                    SupportToolkitMode.Production
                            },

                            MonitoringScope =
                            {
                                ["backup-aggregator"] =
                                    new ModuleMonitoringScopeConfiguration
                                    {
                                        MonitoringItemNumber = Item,
                                        ServiceItemNumbers =
                                            [.. serviceItems ?? []]
                                    }
                            }
                        }
                }
            };

        return new SupportToolkitConfigurationResolver(
            new Store(configuration),
            new NoSecrets(),
            _ => null);
    }

    private sealed class FakeEvidence(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines)
        : ISmartSubscriptionEvidenceSource
    {
        public Task<SmartSubscriptionEvidence> GetEvidenceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new SmartSubscriptionEvidence(headers, lines));
        }
    }

    private sealed class FakeNames(ScopedCustomerParties names)
        : ICustomerNameSource
    {
        public Task<ScopedCustomerParties> GetCustomerPartiesAsync(
            IReadOnlyCollection<string> customerNumbers,
            CancellationToken cancellationToken = default)
        {
            // Like the real source: only the requested customers.
            var requested =
                customerNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

            return Task.FromResult(
                names with
                {
                    Parties =
                        names.Parties
                            .Where(party => requested.Contains(party.Id))
                            .ToArray()
                });
        }
    }

    private sealed class FakeTenants(IReadOnlyList<TenantDto> tenants)
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(tenants);
        }
    }

    private sealed class Store(SupportToolkitConfiguration configuration)
        : ISupportToolkitConfigurationStore
    {
        public string ConfigurationPath => "test-config.json";

        public Task<SupportToolkitConfiguration> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(configuration);
        }

        public Task SaveAsync(
            SupportToolkitConfiguration value,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoSecrets : ISecretStore
    {
        public Task<string?> GetSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task SetSecretAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("unexpected");
        }

        public Task RemoveSecretAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("unexpected");
        }
    }
}
