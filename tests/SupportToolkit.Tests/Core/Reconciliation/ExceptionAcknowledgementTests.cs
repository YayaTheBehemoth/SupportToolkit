using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

public class ExceptionAcknowledgementTests
{
    private const string Item = "ADD-ON";

    private const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly DateOnly AsOf = new(2026, 10, 15);

    [Fact]
    public void ActiveAcknowledgement_HoldsBackTheDiscrepancy()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                Acknowledge(TenantA, AsOf.AddDays(10)));

        Assert.Empty(report.WouldBeTickets);

        var acknowledged = Assert.Single(report.Acknowledged);

        Assert.Equal(
            DiscrepancyType.TenantActiveSubscriptionInactive,
            acknowledged.Key.Type);

        Assert.Equal(AsOf.AddDays(10), acknowledged.Acknowledgement!.ExpiresOn);
        Assert.Equal(0, report.Count(DiscrepancyType.TenantActiveSubscriptionInactive));
    }

    [Fact]
    public void ExpiryDate_IsInclusive()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                Acknowledge(TenantA, AsOf));

        Assert.Empty(report.WouldBeTickets);
        Assert.Single(report.Acknowledged);
    }

    [Fact]
    public void ExpiredAcknowledgement_ResurfacesWithANote()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                Acknowledge(TenantA, AsOf.AddDays(-1)));

        Assert.Empty(report.Acknowledged);

        var discrepancy = Assert.Single(report.WouldBeTickets);

        Assert.False(discrepancy.IsAcknowledged);
        Assert.Equal(AsOf.AddDays(-1), discrepancy.ExpiredAcknowledgement!.ExpiresOn);
    }

    [Fact]
    public void AcknowledgementForAnotherTenant_HasNoEffect()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                Acknowledge(TenantB, AsOf.AddDays(10)));

        var discrepancy = Assert.Single(report.WouldBeTickets);

        Assert.Null(discrepancy.ExpiredAcknowledgement);
    }

    [Fact]
    public void AcknowledgementOfAnotherType_DoesNotHideThisOne()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                new ExceptionAcknowledgement(
                    TenantA,
                    DiscrepancyType.SubscriptionActiveTenantMissing,
                    "Unrelated.",
                    AsOf.AddDays(10)));

        Assert.Equal(
            DiscrepancyType.TenantActiveSubscriptionInactive,
            Assert.Single(report.WouldBeTickets).Key.Type);
    }

    [Fact]
    public void AcknowledgedResult_KeepsTheDiscrepancyBucket()
    {
        var report =
            ReconcileSuspendedOnEnabledTenant(
                Acknowledge(TenantA, AsOf.AddDays(10)));

        Assert.Equal(1, report.Count(ReconciliationBucket.MatchedDiscrepancy));
    }

    [Fact]
    public void TenantMatching_IgnoresCaseAndWhitespace()
    {
        var acknowledgement =
            Acknowledge($" {TenantA.ToUpperInvariant()} ", AsOf);

        Assert.True(
            acknowledgement.Matches(
                TenantA,
                DiscrepancyType.TenantActiveSubscriptionInactive));
    }

    private static ExceptionAcknowledgement Acknowledge(
        string tenantId,
        DateOnly expiresOn)
    {
        return new ExceptionAcknowledgement(
            tenantId,
            DiscrepancyType.TenantActiveSubscriptionInactive,
            "Known data error.",
            expiresOn,
            "reviewer");
    }

    private static ReconciliationReport ReconcileSuspendedOnEnabledTenant(
        ExceptionAcknowledgement acknowledgement)
    {
        return new ReconciliationEngine()
            .Reconcile(
                [Mapping(TenantA, "BC-1", CustomerMappingState.Confirmed)],
                [
                    new SmartSubscriptionHeader(
                        "SUB-1",
                        "BC-1",
                        "Suspended",
                        new DateOnly(2026, 1, 1),
                        null,
                        null,
                        " ")
                ],
                [new SmartSubscriptionLine("SUB-1", Item, "Item", false)],
                [new AcronisTenantObservation(TenantA, true, false)],
                AsOf,
                new HashSet<string> { Item },
                acknowledgements: [acknowledgement]);
    }
}
