using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class ReconciliationEngineTests
{
    private static readonly DateOnly AsOf =
        new(2026, 10, 15);

    [Fact]
    public void AgreeingEvidence_IsMatchedConsistent()
    {
        var mapping =
            Confirmed("t1", "BC-1");

        var result =
            Single(
                Reconcile(
                    [mapping],
                    [Header("SUB-1", "BC-1", "Active")],
                    [Tenant("t1")]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.MatchedConsistent, result.Bucket);
        Assert.Equal(mapping.Id, result.MappingId);
        Assert.Empty(result.Discrepancies);
    }

    [Theory]
    [InlineData("Suspended", " ")]
    [InlineData("Active", "All")]
    public void EnabledTenantWithInactiveSubscription_IsDiscrepancy(
        string status,
        string blocked)
    {
        var mapping =
            Confirmed("t1", "BC-1");

        var report =
            Reconcile(
                [mapping],
                [Header("SUB-1", "BC-1", status, blocked)],
                [Tenant("t1")]
            );

        var result =
            Single(report, ReconciliationSubject.Mapping);

        Assert.Equal(ReconciliationBucket.MatchedDiscrepancy, result.Bucket);

        var discrepancy =
            Assert.Single(report.WouldBeTickets);

        Assert.Equal(
            new DiscrepancyKey(
                mapping.Id,
                DiscrepancyType.TenantActiveSubscriptionInactive,
                "2026-10"),
            discrepancy.Key
        );

        Assert.Equal(["SUB-1"], discrepancy.SubscriptionNumbers);
    }

    [Fact]
    public void OneActiveSubscription_CoversTenant()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [
                        Header("SUB-1", "BC-1", "Active"),
                        Header("SUB-2", "BC-1", "Suspended")
                    ],
                    [Tenant("t1")]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.MatchedConsistent, result.Bucket);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void ActiveSubscriptionWithoutLiveTenant_IsDiscrepancy(
        bool tenantPresent,
        bool deleted,
        bool enabled)
    {
        IReadOnlyList<AcronisTenantObservation> tenants =
            tenantPresent || !enabled
                ? [new AcronisTenantObservation("t1", enabled, deleted)]
                : [];

        var report =
            Reconcile(
                [Confirmed("t1", "BC-1")],
                [Header("SUB-1", "BC-1", "Active")],
                tenants
            );

        Assert.Equal(
            ReconciliationBucket.MatchedDiscrepancy,
            Single(report, ReconciliationSubject.Mapping).Bucket
        );

        Assert.Equal(
            1,
            report.Count(DiscrepancyType.SubscriptionActiveTenantMissing)
        );
    }

    [Fact]
    public void InactiveOnBothSides_IsConsistent()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [Header("SUB-1", "BC-1", "Suspended")],
                    [new AcronisTenantObservation("t1", false, false)]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.MatchedConsistent, result.Bucket);
    }

    [Theory]
    [InlineData("Open", " ")]
    [InlineData("Active", "Invoice")]
    [InlineData("Closed", " ")]
    [InlineData("Active", "Ship")]
    [InlineData("Active", null)]
    public void UndefinedSubscriptionEvidence_IsAmbiguousEvenWithDiscrepancy(
        string status,
        string? blocked)
    {
        var report =
            Reconcile(
                [Confirmed("t1", "BC-1")],
                [
                    Header("SUB-1", "BC-1", status, blocked),
                    Header("SUB-2", "BC-1", "Suspended")
                ],
                [Tenant("t1")]
            );

        Assert.Equal(
            ReconciliationBucket.Ambiguous,
            Single(report, ReconciliationSubject.Mapping).Bucket
        );

        Assert.Empty(report.WouldBeTickets);
    }

    [Fact]
    public void ActiveWithPassedEndDate_IsAmbiguous()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [Header("SUB-1", "BC-1", "Active", endDate: new DateOnly(2026, 1, 1))],
                    [Tenant("t1")]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.Ambiguous, result.Bucket);
    }

    [Fact]
    public void TenantWithoutEnabledState_IsAmbiguous()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [Header("SUB-1", "BC-1", "Active")],
                    [new AcronisTenantObservation("t1", null, false)]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.Ambiguous, result.Bucket);
    }

    [Fact]
    public void ConfirmedCustomerWithoutHeaders_IsAmbiguous()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [],
                    [Tenant("t1")]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.Ambiguous, result.Bucket);
    }

    [Fact]
    public void DuplicateConfirmedMappings_AreAmbiguous()
    {
        var report =
            Reconcile(
                [Confirmed("t1", "BC-1"), Confirmed("t2", "bc-1")],
                [Header("SUB-1", "BC-1", "Active")],
                [Tenant("t1"), Tenant("t2")]
            );

        Assert.All(
            report.Results.Where(
                result => result.Subject == ReconciliationSubject.Mapping),
            result => Assert.Equal(ReconciliationBucket.Ambiguous, result.Bucket)
        );
    }

    [Fact]
    public void DuplicateTenantObservations_AreAmbiguous()
    {
        var result =
            Single(
                Reconcile(
                    [Confirmed("t1", "BC-1")],
                    [Header("SUB-1", "BC-1", "Active")],
                    [Tenant("t1"), Tenant("T1")]
                ),
                ReconciliationSubject.Mapping
            );

        Assert.Equal(ReconciliationBucket.Ambiguous, result.Bucket);
    }

    [Theory]
    [InlineData(CustomerMappingState.Suggested)]
    [InlineData(CustomerMappingState.Rejected)]
    [InlineData(CustomerMappingState.Retired)]
    public void NonConfirmedMappings_AreIgnored_AndBothSidesAreUnscoped(
        CustomerMappingState state)
    {
        var report =
            Reconcile(
                [Mapping("t1", "BC-1", state)],
                [Header("SUB-1", "BC-1", "Suspended")],
                [Tenant("t1")]
            );

        Assert.Equal(2, report.Count(ReconciliationBucket.Unscoped));
        Assert.Equal(2, report.Results.Count);

        Assert.Contains(
            report.Results,
            result =>
                result.Subject == ReconciliationSubject.AcronisTenant
                && result.SubjectId == "t1"
        );

        Assert.Contains(
            report.Results,
            result =>
                result.Subject == ReconciliationSubject.BcCustomer
                && result.SubjectId == "BC-1"
        );
    }

    [Fact]
    public void OrphanLinesAndCustomerlessHeaders_AreUnscoped()
    {
        var report =
            Reconcile(
                [],
                [Header("SUB-1", null, "Active")],
                [new AcronisTenantObservation("t-deleted", true, true)],
                [
                    new SmartSubscriptionLine("SUB-1", "ITEM"),
                    new SmartSubscriptionLine("SUB-ORPHAN", "ITEM"),
                    new SmartSubscriptionLine("SUB-ORPHAN", "OTHER")
                ]
            );

        Assert.Equal(2, report.Results.Count);
        Assert.Equal(2, report.Count(ReconciliationBucket.Unscoped));

        Assert.Contains(
            report.Results,
            result =>
                result.SubjectId == "SUB-ORPHAN"
                && result.Reasons[0].StartsWith("2 subscription line(s)")
        );
    }

    [Fact]
    public void RelevantItems_LimitReconciledSubscriptions()
    {
        var report =
            Reconcile(
                [Confirmed("t1", "BC-1")],
                [
                    Header("SUB-1", "BC-1", "Active"),
                    Header("SUB-2", "BC-1", "Open")
                ],
                [Tenant("t1")],
                [
                    new SmartSubscriptionLine("SUB-1", "BACKUP"),
                    new SmartSubscriptionLine("SUB-2", "UNRELATED")
                ],
                new HashSet<string> { "BACKUP" }
            );

        Assert.Equal(
            ReconciliationBucket.MatchedConsistent,
            Single(report, ReconciliationSubject.Mapping).Bucket
        );
    }

    [Fact]
    public void EveryResult_HasExactlyOneBucket_AndOnlyDiscrepanciesAreTicketable()
    {
        var report =
            Reconcile(
                [Confirmed("t1", "BC-1"), Confirmed("t2", "BC-2")],
                [
                    Header("SUB-1", "BC-1", "Suspended"),
                    Header("SUB-2", "BC-2", "Open"),
                    Header("SUB-3", "BC-3", "Active")
                ],
                [Tenant("t1"), Tenant("t2"), Tenant("t3")]
            );

        Assert.Equal(
            report.Results.Count,
            Enum.GetValues<ReconciliationBucket>().Sum(report.Count)
        );

        Assert.Equal(1, report.Count(ReconciliationBucket.MatchedDiscrepancy));
        Assert.Equal(1, report.Count(ReconciliationBucket.Ambiguous));
        Assert.Equal(2, report.Count(ReconciliationBucket.Unscoped));

        Assert.All(
            report.Results.Where(
                result => result.Bucket != ReconciliationBucket.MatchedDiscrepancy),
            result => Assert.Empty(result.Discrepancies)
        );
    }

    private static ReconciliationReport Reconcile(
        IReadOnlyList<CustomerMappingRecord> mappings,
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<AcronisTenantObservation> tenants,
        IReadOnlyList<SmartSubscriptionLine>? lines = null,
        IReadOnlySet<string>? relevantItemNumbers = null)
    {
        return new ReconciliationEngine()
            .Reconcile(
                mappings,
                headers,
                lines ?? [],
                tenants,
                AsOf,
                relevantItemNumbers
            );
    }

    private static ReconciliationResult Single(
        ReconciliationReport report,
        ReconciliationSubject subject)
    {
        return Assert.Single(
            report.Results,
            result => result.Subject == subject
        );
    }

    private static CustomerMappingRecord Confirmed(
        string tenant,
        string customer)
    {
        return Mapping(tenant, customer, CustomerMappingState.Confirmed);
    }

    private static AcronisTenantObservation Tenant(
        string id)
    {
        return new AcronisTenantObservation(id, true, false);
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string? customer,
        string status,
        string? blocked = " ",
        DateOnly? endDate = null)
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            endDate,
            customer,
            blocked
        );
    }
}
