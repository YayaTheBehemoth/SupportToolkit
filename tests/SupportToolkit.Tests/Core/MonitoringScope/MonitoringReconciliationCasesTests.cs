using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

/// <summary>
/// Synthetic regression cases from a manual reconciliation:
/// item list entitlement, per-customer decisions, monitored versus
/// entitled tenants, exclusions and empty-value handling.
/// </summary>
public class MonitoringReconciliationCasesTests
{
    private const string Entitled = "BAASOVERVÅG";
    private const string EntitledDecomposed = "BAASOVERVÅG";
    private const string BackupOnly = "BAAS";
    private const string SpacedItem = "SECURE BACKUP";

    private const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string OwnTenant = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    private static readonly DateOnly AsOf = new(2026, 10, 15);

    // Case 1

    [Fact]
    public void Evaluator_AcceptsAListOfItemNumbers()
    {
        var evaluator =
            new SmartSubscriptionEntitlementEvaluator(
                [Entitled, SpacedItem]);

        var results =
            evaluator.Evaluate(
                [Header("SUB-1", "BC-1"), Header("SUB-2", "BC-2")],
                [Line("SUB-1", Entitled), Line("SUB-2", SpacedItem)],
                AsOf);

        Assert.All(
            results,
            result => Assert.Equal(
                MonitoringEntitlementAssessment.ActiveCandidate,
                result.Assessment));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Evaluator_RejectsEmptyOrBlankLists()
    {
        Assert.Throws<ArgumentException>(
            () => new SmartSubscriptionEntitlementEvaluator(
                Array.Empty<string>()));

        Assert.Throws<ArgumentException>(
            () => new SmartSubscriptionEntitlementEvaluator(
                [Entitled, "  "]));
    }

    [Fact]
    public void DefaultList_IsTheMonitoringAddOnOnly()
    {
        Assert.Equal(
            [Entitled],
            SubscriptionItemNumbers.DefaultMonitoringItemNumbers);
    }

    // Case 2

    [Theory]
    [InlineData(EntitledDecomposed)]
    [InlineData("  " + Entitled + "  ")]
    [InlineData("baasovervåg")]
    public void ItemNumbers_MatchAcrossNormalizationTrimAndCase(
        string lineItem)
    {
        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator([Entitled])
                    .Evaluate(
                        [Header("SUB-1", "BC-1")],
                        [Line("SUB-1", lineItem)],
                        AsOf));

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Fact]
    public void ConfiguredDecomposedSpelling_MatchesPrecomposedLine()
    {
        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator(
                    [EntitledDecomposed])
                    .Evaluate(
                        [Header("SUB-1", "BC-1")],
                        [Line("SUB-1", Entitled)],
                        AsOf));

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    [Fact]
    public void ItemNumbersWithSpaces_KeepInternalSpacesSignificant()
    {
        var evaluator =
            new SmartSubscriptionEntitlementEvaluator([SpacedItem]);

        Assert.Single(
            evaluator.Evaluate(
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", SpacedItem)],
                AsOf));

        Assert.Empty(
            evaluator.Evaluate(
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", "SECUREBACKUP")],
                AsOf));
    }

    [Fact]
    public void BackupItemWithoutTheAddOn_IsNotEntitlementEvidence()
    {
        Assert.Empty(
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [Header("SUB-1", "BC-1")],
                    [Line("SUB-1", BackupOnly)],
                    AsOf));
    }

    // Case 3

    [Fact]
    public void ActiveSubscription_DecidesOverSuspendedSibling_InSelection()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [
                        Header("SUB-1", "BC-1"),
                        Header("SUB-2", "BC-1", status: "Suspended")
                    ],
                    [Line("SUB-1", Entitled), Line("SUB-2", Entitled)],
                    AsOf);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [Tenant(TenantA)],
                    observations,
                    [new("BC-1", TenantA)]);

        Assert.Single(result.InventoryCandidates);
    }

    // Decided 2026-10-27: an entitling item only on a Suspended
    // subscription does not entitle.
    [Fact]
    public void SuspendedOnly_IsNotEntitled()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [Header("SUB-2", "BC-1", status: "Suspended")],
                    [Line("SUB-2", Entitled)],
                    AsOf);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [Tenant(TenantA)],
                    observations,
                    [new("BC-1", TenantA)]);

        Assert.Empty(result.InventoryCandidates);
        Assert.Equal(0, result.NeedsReviewCount);

        var finding = Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.NotEntitled,
            finding.Status);

        Assert.Equal("BC-1", finding.CustomerNumber);
    }

    [Fact]
    public void AddOnWithoutBackupItem_IsEntitled()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [Header("SUB-1", "BC-1")],
                    [Line("SUB-1", Entitled)],
                    AsOf);

        var observation = Assert.Single(observations);

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            observation.Assessment);

        Assert.Equal(Entitled, observation.ItemNumber);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [Tenant(TenantA)],
                    observations,
                    [new("BC-1", TenantA)]);

        var finding = Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.CandidateForInventory,
            finding.Status);

        Assert.Equal("BC-1", finding.CustomerNumber);

        Assert.Equal(
            new EntitlingSubscription("SUB-1", Entitled),
            Assert.Single(finding.EntitlingSubscriptions));
    }

    [Theory]
    [InlineData("All")]
    [InlineData("Invoice")]
    public void BlockedSibling_StillBlocksTheDecision(
        string blocked)
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [
                        Header("SUB-1", "BC-1"),
                        Header("SUB-2", "BC-1", blocked: blocked)
                    ],
                    [Line("SUB-1", Entitled), Line("SUB-2", Entitled)],
                    AsOf);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [Tenant(TenantA)],
                    observations,
                    [new("BC-1", TenantA)]);

        Assert.Empty(result.InventoryCandidates);
        Assert.Equal(1, result.NeedsReviewCount);
    }

    [Fact]
    public void ActiveAndSuspended_IsConsistentInReconciliation()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [
                    Header("SUB-1", "BC-1"),
                    Header("SUB-2", "BC-1", status: "Suspended")
                ],
                [Line("SUB-1", Entitled), Line("SUB-2", Entitled)],
                [Observed(TenantA)],
                monitored: [TenantA]);

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            MappingResult(report).Bucket);
    }

    // Case 4

    [Fact]
    public void BackupWithoutAddOn_ButMonitored_IsDiscrepancy()
    {
        var mapping = Confirmed(TenantA, "BC-1");

        var report =
            Reconcile(
                [mapping],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", BackupOnly)],
                [Observed(TenantA)],
                monitored: [TenantA]);

        Assert.Equal(
            ReconciliationBucket.MatchedDiscrepancy,
            MappingResult(report).Bucket);

        var discrepancy = Assert.Single(report.WouldBeTickets);

        Assert.Equal(
            DiscrepancyType.MonitoredWithoutEntitlement,
            discrepancy.Key.Type);

        Assert.Equal(mapping.Id, discrepancy.Key.MappingId);
    }

    // Decided 2026-10-27: not entitled and not monitored agree.
    [Fact]
    public void NoEntitledSubscription_AndNotMonitored_IsConsistent()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", BackupOnly)],
                [Observed(TenantA)],
                monitored: []);

        var result = MappingResult(report);

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            result.Bucket);

        Assert.Equal(
            "Not entitled and not monitored.",
            Assert.Single(result.Reasons));

        Assert.Empty(report.WouldBeTickets);
    }

    [Fact]
    public void WithoutMonitoredEvidence_NoMonitoredDiscrepancyIsInvented()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", BackupOnly)],
                [Observed(TenantA)],
                monitored: null);

        var result = MappingResult(report);

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            result.Bucket);

        Assert.Equal(
            "Not entitled; monitored status unknown.",
            Assert.Single(result.Reasons));

        Assert.Empty(report.WouldBeTickets);
    }

    [Fact]
    public void EntitledButNotMonitored_StaysAmbiguous()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", Entitled)],
                [Observed(TenantA)],
                monitored: []);

        Assert.Equal(
            ReconciliationBucket.Ambiguous,
            MappingResult(report).Bucket);
    }

    [Fact]
    public void MonitoredWithAddOnOnlyOnDisabledTenantsSuspendedSubscription_IsMonitoredWithoutEntitlement()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1", status: "Suspended")],
                [Line("SUB-1", Entitled)],
                [new AcronisTenantObservation(TenantA, false, false)],
                monitored: [TenantA]);

        Assert.Equal(
            DiscrepancyType.MonitoredWithoutEntitlement,
            Assert.Single(report.WouldBeTickets).Key.Type);
    }

    [Fact]
    public void MonitoredButTenantNotFound_IsAmbiguous()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", BackupOnly)],
                [],
                monitored: [TenantA]);

        Assert.Equal(
            ReconciliationBucket.Ambiguous,
            MappingResult(report).Bucket);
    }

    // Case 5

    [Fact]
    public void SuspendedSubscription_WithActiveTenant_IsDiscrepancy()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1", status: "Suspended")],
                [Line("SUB-1", Entitled)],
                [Observed(TenantA)],
                monitored: [TenantA]);

        Assert.Equal(
            ReconciliationBucket.MatchedDiscrepancy,
            MappingResult(report).Bucket);

        Assert.Equal(
            DiscrepancyType.TenantActiveSubscriptionInactive,
            Assert.Single(report.WouldBeTickets).Key.Type);
    }

    // Case 6

    [Fact]
    public void ActiveAddOn_AndMonitored_IsConsistent()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", EntitledDecomposed)],
                [Observed(TenantA)],
                monitored: [TenantA]);

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            MappingResult(report).Bucket);

        Assert.Empty(report.WouldBeTickets);
    }

    [Fact]
    public void ActiveAddOn_ButNotMonitored_IsNeverConsistent()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", Entitled)],
                [Observed(TenantA)],
                monitored: []);

        Assert.Equal(
            ReconciliationBucket.Ambiguous,
            MappingResult(report).Bucket);
    }

    // Case 7

    [Fact]
    public void ExcludedTenant_IsNeitherUnscopedNorDiscrepancy()
    {
        var report =
            Reconcile(
                [],
                [Header("SUB-1", "BC-1")],
                [Line("SUB-1", Entitled)],
                [Observed(OwnTenant), Observed(TenantA)],
                monitored: [OwnTenant],
                excluded: [OwnTenant]);

        Assert.DoesNotContain(
            report.Results,
            result => result.SubjectId == OwnTenant);

        Assert.Single(
            report.Results,
            result => result.SubjectId == TenantA
                && result.Bucket == ReconciliationBucket.Unscoped);
    }

    [Fact]
    public void ExcludedTenant_WithAMapping_IsNotReconciled()
    {
        var report =
            Reconcile(
                [Confirmed(OwnTenant, "BC-OWN")],
                [],
                [],
                [Observed(OwnTenant)],
                monitored: [OwnTenant],
                excluded: [OwnTenant.ToUpperInvariant()]);

        Assert.Empty(report.Results);
    }

    [Fact]
    public void ExcludedTenant_IsDroppedFromSelection()
    {
        var result =
            new MonitoringTenantSelector()
                .Select(
                    [Tenant(OwnTenant), Tenant(TenantA)],
                    [],
                    [],
                    new HashSet<string> { OwnTenant });

        var finding = Assert.Single(result.Tenants);

        Assert.Equal(TenantA, finding.Tenant.Id);
    }

    // Operator's own company: partner + customer + unit tenants

    private const string OwnName = "SYNTHETIC OPERATOR";
    private const string OwnCustomerName = "Synthetic Operator ";

    [Fact]
    public void OperatorCustomerTenant_IsExcludedByPartnerName()
    {
        var result =
            new MonitoringTenantSelector()
                .Select(
                    [
                        Tenant(TenantB, OwnName, "partner"),
                        Tenant(OwnTenant, OwnCustomerName),
                        Tenant(TenantA, "Synthetic Customer"),
                        Tenant(Guid.NewGuid().ToString(), OwnName, "unit")
                    ],
                    [],
                    []);

        Assert.Equal(TenantA, Assert.Single(result.Tenants).Tenant.Id);
        Assert.Equal(1, result.ExcludedTenantCount);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void PartnerAndUnitTenants_NeverReachSelectionOrObservations()
    {
        var tenants =
            new[]
            {
                Tenant(TenantB, OwnName, "partner"),
                Tenant(TenantA, "Synthetic Customer", "unit")
            };

        Assert.Empty(
            new MonitoringTenantSelector()
                .Select(tenants, [], []).Tenants);

        Assert.Empty(
            AcronisTenantObservations.FromCustomerTenants(tenants));
    }

    [Fact]
    public void NearMissName_IsNotExcluded()
    {
        var result =
            new MonitoringTenantSelector()
                .Select(
                    [
                        Tenant(TenantB, OwnName, "partner"),
                        Tenant(OwnTenant, "Synthetic Operator Holding")
                    ],
                    [],
                    []);

        Assert.Single(result.Tenants);
        Assert.Equal(0, result.ExcludedTenantCount);
    }

    [Fact]
    public void NoPartnerTenant_ExcludesNothing()
    {
        var result =
            new MonitoringTenantSelector()
                .Select([Tenant(OwnTenant, OwnName)], [], []);

        Assert.Single(result.Tenants);
        Assert.Equal(0, result.ExcludedTenantCount);
    }

    [Fact]
    public void SeveralCustomersMatchingThePartner_ExcludesNoneAndNotices()
    {
        var result =
            new MonitoringTenantSelector()
                .Select(
                    [
                        Tenant(TenantB, OwnName, "partner"),
                        Tenant(OwnTenant, OwnName),
                        Tenant(TenantA, OwnName.ToLowerInvariant())
                    ],
                    [],
                    []);

        Assert.Equal(2, result.Tenants.Count);
        Assert.Equal(0, result.ExcludedTenantCount);
        Assert.Single(result.Notices);
    }

    [Fact]
    public void OperatorMatchWithBusinessCentralIdentity_IsReviewedNotHidden()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator([Entitled])
                .Evaluate(
                    [Header("SUB-1", "BC-1")],
                    [Line("SUB-1", Entitled)],
                    AsOf);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [
                        Tenant(TenantB, OwnName, "partner"),
                        Tenant(OwnTenant, OwnName)
                    ],
                    observations,
                    [new("BC-1", OwnTenant)]);

        var finding = Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.NeedsReview,
            finding.Status);

        Assert.Empty(result.InventoryCandidates);
        Assert.Equal(0, result.ExcludedTenantCount);
        Assert.Single(result.Notices);
    }

    // Case 8

    [Fact]
    public void LinesWithoutHeader_AreUnscopedAndDoNotCrash()
    {
        var report =
            Reconcile(
                [Confirmed(TenantA, "BC-1")],
                [Header("SUB-1", "BC-1")],
                [
                    Line("SUB-1", Entitled),
                    Line("SUB-MISSING", BackupOnly),
                    Line("SUB-MISSING", Entitled),
                    Line("", BackupOnly)
                ],
                [Observed(TenantA)],
                monitored: [TenantA]);

        Assert.Contains(
            report.Results,
            result =>
                result.Subject == ReconciliationSubject.BcSubscription
                && result.SubjectId == "SUB-MISSING"
                && result.Bucket == ReconciliationBucket.Unscoped);

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            MappingResult(report).Bucket);
    }

    [Fact]
    public void EvaluatorLineWithoutHeader_NeedsReviewWithoutCrash()
    {
        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator([Entitled])
                    .Evaluate(
                        [],
                        [Line("SUB-MISSING", Entitled)],
                        AsOf));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Null(result.CustomerNumber);
    }

    // Case 9

    [Fact]
    public void EmptyDateAndBlankEnum_MapToNone_NotRealValues()
    {
        var headers =
            BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "SUB-1",
                    SellToCustomerNumber = "BC-1",
                    Status = "Active",
                    Blocked = "_x0020_",
                    StartDate = "2026-01-01",
                    EndDate = "0001-01-01"
                }
            ]);

        var header = Assert.Single(headers);

        Assert.Null(header.EndDate);
        Assert.Equal(
            SubscriptionBlockedState.NotBlocked,
            SmartSubscriptionStates.ParseBlocked(header.BlockedValue));
    }

    [Fact]
    public void EmptyStartDate_IsNeverTreatedAsAValidStart()
    {
        var headers =
            BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "SUB-1",
                    SellToCustomerNumber = "BC-1",
                    Status = "Active",
                    Blocked = "_x0020_",
                    StartDate = "0001-01-01",
                    EndDate = "0001-01-01"
                }
            ]);

        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator([Entitled])
                    .Evaluate(headers, [Line("SUB-1", Entitled)], AsOf));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);

        Assert.Contains("start date", result.Reason);
    }

    [Fact]
    public void BlankLineType_IsNotAnItemLine()
    {
        var lines =
            BusinessCentralSubscriptionMapper.MapLines(
            [
                new BusinessCentralSubscriptionLineDto
                {
                    SubscriptionNumber = "SUB-1",
                    Number = Entitled,
                    Type = "_x0020_",
                    OmitLine = false
                }
            ],
            new BusinessCentralSourceContext("Sandbox", "Synthetic"));

        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator([Entitled])
                    .Evaluate(
                        [Header("SUB-1", "BC-1")],
                        lines,
                        AsOf));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);
    }

    [Fact]
    public void SellToIsTheCustomerKey_AndMissingBillToIsHarmless()
    {
        var headers =
            BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "SUB-1",
                    SellToCustomerNumber = "BC-1",
                    BillToCustomerNumber = "",
                    Status = "Active",
                    Blocked = "_x0020_",
                    StartDate = "2026-01-01",
                    EndDate = "0001-01-01"
                }
            ]);

        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator([Entitled])
                    .Evaluate(headers, [Line("SUB-1", Entitled)], AsOf));

        Assert.Equal("BC-1", result.CustomerNumber);
        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            result.Assessment);
    }

    private static ReconciliationReport Reconcile(
        IReadOnlyList<CustomerMappingRecord> mappings,
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines,
        IReadOnlyList<AcronisTenantObservation> tenants,
        string[]? monitored = null,
        string[]? excluded = null)
    {
        return new ReconciliationEngine()
            .Reconcile(
                mappings,
                headers,
                lines,
                tenants,
                AsOf,
                new HashSet<string> { EntitledDecomposed },
                excluded?.ToHashSet(),
                monitored?.ToHashSet());
    }

    private static ReconciliationResult MappingResult(
        ReconciliationReport report)
    {
        return Assert.Single(
            report.Results,
            result => result.Subject == ReconciliationSubject.Mapping);
    }

    private static CustomerMappingRecord Confirmed(
        string tenant,
        string customer)
    {
        return Mapping(tenant, customer, CustomerMappingState.Confirmed);
    }

    private static AcronisTenantObservation Observed(
        string id)
    {
        return new AcronisTenantObservation(id, true, false);
    }

    private static TenantDto Tenant(
        string id,
        string name = "Synthetic Customer",
        string kind = "customer")
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind
        };
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status = "Active",
        string blocked = " ")
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            null,
            blocked);
    }

    private static SmartSubscriptionLine Line(
        string subscription,
        string item)
    {
        return new SmartSubscriptionLine(
            subscription,
            item,
            "Item",
            false);
    }
}
