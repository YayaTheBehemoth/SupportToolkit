
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests;

public class MonitoringTenantSelectorTests
{
    private readonly MonitoringTenantSelector _selector = new();

    private static readonly DateOnly EvaluationDate =
        new(2026, 10, 15);

    [Fact]
    public void StandardFixture_ProducesThreeCandidatesAndVisibleExceptions()
    {
        var subscriptions =
            SmartSubscriptionFixtureFactory.Create();

        var acronis =
            MonitoringTenantSelectionFixtureFactory.Create();

        var observations =
            new SmartSubscriptionEntitlementEvaluator(
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            ).Evaluate(
                subscriptions.Headers,
                subscriptions.Lines,
                EvaluationDate
            );

        var result =
            _selector.Select(
                acronis.Tenants,
                observations,
                acronis.Identities
            );

        Assert.Equal(5, result.Tenants.Count);
        Assert.Equal(3, result.InventoryCandidates.Count);
        Assert.Equal(1, result.NeedsReviewCount);
        Assert.Equal(1, result.UnclassifiedCount);
        Assert.Empty(result.UnmatchedEvidence);
    }

    [Fact]
    public void TenantWithoutIdentity_IsUnclassified()
    {
        var result =
            _selector.Select(
                [Tenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")],
                [Active("BC-001")],
                []
            );

        var finding =
            Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.Unclassified,
            finding.Status
        );

        Assert.Single(result.UnmatchedEvidence);
    }

    [Fact]
    public void MissingEntitlement_IsNotAutomaticallyExcluded()
    {
        var tenant =
            Tenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var result =
            _selector.Select(
                [tenant],
                [],
                [
                    new(
                        "BC-001",
                        tenant.Id
                    )
                ]
            );

        var finding =
            Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.Unclassified,
            finding.Status
        );
    }

    [Fact]
    public void AmbiguousIdentity_IsNotSelected()
    {
        var tenant =
            Tenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var result =
            _selector.Select(
                [tenant],
                [
                    Active("BC-001"),
                    Active("BC-002")
                ],
                [
                    new("BC-001", tenant.Id),
                    new("BC-002", tenant.Id)
                ]
            );

        var finding =
            Assert.Single(result.Tenants);

        Assert.Equal(
            MonitoringTenantSelectionStatus.NeedsReview,
            finding.Status
        );

        Assert.Empty(result.InventoryCandidates);
    }

    [Fact]
    public void ConflictingCustomerEvidence_IsNotSelected()
    {
        var tenant =
            Tenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var result =
            _selector.Select(
                [tenant],
                [
                    Active("BC-001"),
                    new(
                        "SUB-002",
                        "BC-001",
                        MonitoringEntitlementAssessment.NeedsReview,
                        "Blocked subscription."
                    )
                ],
                [
                    new("BC-001", tenant.Id)
                ]
            );

        Assert.Equal(
            MonitoringTenantSelectionStatus.NeedsReview,
            Assert.Single(result.Tenants).Status
        );
    }

    [Fact]
    public void OneCustomerMayResolveToMultipleAcronisTenants()
    {
        var alpha =
            Tenant("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var beta =
            Tenant("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var result =
            _selector.Select(
                [alpha, beta],
                [Active("BC-001")],
                [
                    new("BC-001", alpha.Id),
                    new("BC-001", beta.Id)
                ]
            );

        Assert.Equal(
            2,
            result.InventoryCandidates.Count
        );

        Assert.Empty(result.UnmatchedEvidence);
    }

    [Fact]
    public void TenantNameDoesNotParticipateInIdentityMatching()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "An entirely unrelated display name"
            );

        var result =
            _selector.Select(
                [tenant],
                [Active("BC-001")],
                [
                    new("BC-001", tenant.Id)
                ]
            );

        Assert.Single(result.InventoryCandidates);
    }

    [Fact]
    public void MissingAcronisTenantLeavesEntitlementUnmatched()
    {
        var result =
            _selector.Select(
                [],
                [Active("BC-001")],
                [
                    new(
                        "BC-001",
                        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                    )
                ]
            );

        Assert.Empty(result.Tenants);
        Assert.Single(result.UnmatchedEvidence);
    }

    [Fact]
    public void DuplicateAcronisTenantIds_RequireReview()
    {
        var tenantId =
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

        var result =
            _selector.Select(
                [
                    Tenant(tenantId, "First"),
                    Tenant(tenantId, "Second")
                ],
                [Active("BC-001")],
                [
                    new("BC-001", tenantId)
                ]
            );

        Assert.Equal(2, result.NeedsReviewCount);
        Assert.Empty(result.InventoryCandidates);
    }

    [Fact]
    public void NonCustomerTenants_AreNotInventoryCandidates()
    {
        var partner =
            new TenantDto
            {
                Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                Name = "Synthetic Partner",
                Kind = "partner"
            };

        var result =
            _selector.Select(
                [partner],
                [Active("BC-001")],
                [
                    new("BC-001", partner.Id)
                ]
            );

        Assert.Empty(result.Tenants);
        Assert.Single(result.UnmatchedEvidence);
    }

    private static MonitoringEntitlementObservation Active(
        string customerNumber)
    {
        return new MonitoringEntitlementObservation(
            "SUB-001",
            customerNumber,
            MonitoringEntitlementAssessment.ActiveCandidate,
            "Synthetic active entitlement."
        );
    }

    private static TenantDto Tenant(
        string id,
        string name = "Synthetic Customer")
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = "customer"
        };
    }
}
