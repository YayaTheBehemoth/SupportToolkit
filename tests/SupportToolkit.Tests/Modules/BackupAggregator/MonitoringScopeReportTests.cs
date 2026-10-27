using System.Globalization;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

using static SupportToolkit.Tests.CustomerMappingTestSupport;

namespace SupportToolkit.Tests;

/// <summary>
/// Synthetic tenants only: one each of in scope, configured exclusion,
/// operator tenant, not entitled, needs review and unscoped.
/// </summary>
public class MonitoringScopeReportTests
{
    private const string Item = "ADD-ON";

    private const string InScopeId = "10000000-0000-0000-0000-000000000001";
    private const string ConfiguredId = "10000000-0000-0000-0000-000000000002";
    private const string OperatorId = "10000000-0000-0000-0000-000000000003";
    private const string NotEntitledId = "10000000-0000-0000-0000-000000000004";
    private const string ReviewId = "10000000-0000-0000-0000-000000000005";
    private const string UnmappedId = "10000000-0000-0000-0000-000000000006";

    private static readonly DateOnly AsOf = new(2026, 10, 15);

    [Fact]
    public void Selection_ListsExcludedTenantsWithReasons()
    {
        var selection = Select();

        Assert.Equal(2, selection.ExcludedTenantCount);

        Assert.Contains(
            selection.ExcludedTenants,
            excluded =>
                excluded.Tenant.Id == ConfiguredId
                && excluded.Reason == MonitoringTenantExclusionReason.Configured);

        Assert.Contains(
            selection.ExcludedTenants,
            excluded =>
                excluded.Tenant.Id == OperatorId
                && excluded.Reason == MonitoringTenantExclusionReason.OperatorTenant);
    }

    [Fact]
    public void Report_PartitionsEveryCustomerTenantExactlyOnce()
    {
        var report = Build();

        Assert.Equal(6, report.AcronisCustomerTenants);
        Assert.Single(report.InScope);
        Assert.Equal(3, report.Excluded.Count);
        Assert.Single(report.NeedsReview);
        Assert.Single(report.UnscopedTenants);

        var ids =
            report.InScope.Select(tenant => tenant.TenantId)
                .Concat(report.Excluded.Select(tenant => tenant.TenantId))
                .Concat(report.NeedsReview.Select(tenant => tenant.TenantId))
                .Concat(report.UnscopedTenants.Select(tenant => tenant.TenantId))
                .ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void InScopeTenant_NamesCustomerAndEntitlingSubscription()
    {
        var tenant = Assert.Single(Build().InScope);

        Assert.Equal("In Scope Co", tenant.TenantName);
        Assert.Equal("BC-IN", tenant.CustomerNumber);

        Assert.Equal(
            new EntitlingSubscription("SUB-IN", Item),
            Assert.Single(tenant.EntitlingSubscriptions));
    }

    [Fact]
    public void NotEntitledTenant_IsExcludedAndItsDiscrepancyStillReported()
    {
        var report = Build();

        var excluded =
            Assert.Single(
                report.Excluded,
                tenant => tenant.Reason == ScopeExclusionReason.NotEntitled);

        Assert.Equal(NotEntitledId, excluded.TenantId);

        var discrepancy =
            Assert.Single(
                report.Discrepancies,
                item => item.TenantId == NotEntitledId);

        Assert.Equal(
            DiscrepancyType.TenantActiveSubscriptionInactive,
            discrepancy.Discrepancy.Key.Type);
    }

    [Fact]
    public void Console_WritesSummaryAndListings()
    {
        using var writer =
            new StringWriter(CultureInfo.InvariantCulture);

        new ConsoleMonitoringScopeReporter().Write(writer, Build());

        var output = writer.ToString();

        Assert.Contains("Entitling items: ADD-ON", output);
        Assert.Contains("Acronis customer tenants:  6", output);
        Assert.Contains("  In scope this run:       1", output);
        Assert.Contains("  Excluded:                3", output);
        Assert.Contains("Entitled BC customers:     1", output);
        Assert.Contains("Discrepancies (open):      2", output);
        Assert.Contains("monitored without entitlement", output);

        Assert.Contains("  In Scope Co | BC-IN | SUB-IN (ADD-ON)", output);
        Assert.Contains("  Operator Co | Operator's own tenant.", output);
        Assert.Contains("  Configured Co | Excluded by configuration.", output);

        Assert.Contains(
            "  Suspended Co | BC-SUSP | Not entitled. Entitling item only " +
            "on suspended subscriptions.",
            output);

        Assert.Contains(
            "  Suspended Co | BC-SUSP | TenantActiveSubscriptionInactive | " +
            "SUB-SUSP |",
            output);

        Assert.Contains("  Needs scope review | Review Co |", output);
        Assert.Contains("  Unscoped tenant | Unmapped Co |", output);
    }

    [Fact]
    public void WithoutReconciliation_SaysSoInsteadOfReportingNone()
    {
        var selection = Select();

        var report =
            MonitoringScopeReportBuilder.Build(
                AsOf,
                [Item],
                Observations(),
                selection,
                reconciliation: null,
                monitoredEvidenceSupplied: false,
                Tenants());

        using var writer =
            new StringWriter(CultureInfo.InvariantCulture);

        new ConsoleMonitoringScopeReporter().Write(writer, report);

        var output = writer.ToString();

        Assert.False(report.ReconciliationRun);
        Assert.Contains("Reconciliation:            not run", output);
        Assert.Contains("  Reconciliation was not run.", output);
    }

    private static MonitoringScopeReport Build()
    {
        var selection = Select();

        var reconciliation =
            new ReconciliationEngine()
                .Reconcile(
                    Mappings(),
                    Headers(),
                    Lines(),
                    [.. Tenants()
                        .Where(tenant => tenant.Kind == "customer")
                        .Select(tenant => new AcronisTenantObservation(tenant.Id, true, false))],
                    AsOf,
                    new HashSet<string> { Item },
                    selection.ExcludedTenants
                        .Select(excluded => excluded.Tenant.Id)
                        .ToHashSet());

        return MonitoringScopeReportBuilder.Build(
            AsOf,
            [Item],
            Observations(),
            selection,
            reconciliation,
            monitoredEvidenceSupplied: false,
            Tenants());
    }

    private static MonitoringTenantSelectionResult Select()
    {
        return new MonitoringTenantSelector()
            .Select(
                Tenants(),
                Observations(),
                Mappings()
                    .Select(
                        mapping =>
                            new ResolvedMonitoringCustomerIdentity(
                                mapping.BcCustomerNo,
                                mapping.AcronisTenantId))
                    .ToArray(),
                new HashSet<string> { ConfiguredId });
    }

    private static IReadOnlyList<MonitoringEntitlementObservation> Observations()
    {
        return new SmartSubscriptionEntitlementEvaluator([Item])
            .Evaluate(Headers(), Lines(), AsOf);
    }

    private static IReadOnlyList<CustomerMappingRecord> Mappings()
    {
        return
        [
            Mapping(InScopeId, "BC-IN", CustomerMappingState.Confirmed),
            Mapping(NotEntitledId, "BC-SUSP", CustomerMappingState.Confirmed),
            Mapping(ReviewId, "BC-REVIEW", CustomerMappingState.Confirmed)
        ];
    }

    private static IReadOnlyList<SmartSubscriptionHeader> Headers()
    {
        return
        [
            Header("SUB-IN", "BC-IN", "Active", " "),
            Header("SUB-SUSP", "BC-SUSP", "Suspended", " "),
            Header("SUB-REVIEW", "BC-REVIEW", "Active", "All")
        ];
    }

    private static IReadOnlyList<SmartSubscriptionLine> Lines()
    {
        return
        [
            new("SUB-IN", Item, "Item", false),
            new("SUB-SUSP", Item, "Item", false),
            new("SUB-REVIEW", Item, "Item", false)
        ];
    }

    private static IReadOnlyList<TenantDto> Tenants()
    {
        return
        [
            Tenant(InScopeId, "In Scope Co"),
            Tenant(ConfiguredId, "Configured Co"),
            Tenant("20000000-0000-0000-0000-000000000001", "Operator Co", "partner"),
            Tenant(OperatorId, "Operator Co"),
            Tenant(NotEntitledId, "Suspended Co"),
            Tenant(ReviewId, "Review Co"),
            Tenant(UnmappedId, "Unmapped Co")
        ];
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status,
        string blocked)
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            customer,
            blocked);
    }

    private static TenantDto Tenant(
        string id,
        string name,
        string kind = "customer")
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind,
            Enabled = true
        };
    }
}
