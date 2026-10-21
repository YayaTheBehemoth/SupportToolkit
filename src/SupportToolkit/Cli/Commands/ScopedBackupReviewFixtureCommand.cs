using SupportToolkit.Core.MonitoringScope;

using System.Collections.Concurrent;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Demonstrates scoped inventory execution with synthetic data.
///
/// This command uses in-memory providers only.
/// It never creates an external HTTP session.
/// </summary>
public sealed class ScopedBackupReviewFixtureCommand
{
    public async Task<int> RunAsync(
        TextWriter output,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);

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
                asOfDate
            );

        var selection =
            new MonitoringTenantSelector()
                .Select(
                    acronis.Tenants,
                    observations,
                    acronis.Identities
                );

        var fixtureInventory =
            new HealthyFixtureInventoryProvider(
                asOfDate
            );

        /*
         * Deliberately use a tenant provider that throws if enumerated.
         *
         * The scoped execution must use the supplied selection,
         * not fall back to an all-tenant discovery.
         */
        var reviewService =
            new BackupInventoryReviewService(
                new AcronisTenantResolver(
                    new NoTenantDiscoveryProvider()
                ),
                fixtureInventory,
                fixtureInventory,
                new BackupInventoryNormalizer()
            );

        var result =
            await reviewService.ReviewSelectedTenantsAsync(
                selection,
                cancellationToken
            );

        var expectedTenantIds =
            selection.InventoryCandidates
                .Select(
                    tenant => tenant.Id
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        /*
         * Guard the demonstration itself against unintentionally
         * requesting inventory for non-selected tenants.
         */
        if (!fixtureInventory.Microsoft365Requests
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expectedTenantIds)
            || !fixtureInventory.DeviceRequests
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(expectedTenantIds)
            || fixtureInventory.Microsoft365Requests.Count
                != expectedTenantIds.Count
            || fixtureInventory.DeviceRequests.Count
                != expectedTenantIds.Count)
        {
            throw new InvalidOperationException(
                "Fixture inventory requested an unexpected tenant."
            );
        }

        output.WriteLine();
        output.WriteLine(
            "SCOPED BACKUP INVENTORY - FIXTURE REVIEW"
        );
        output.WriteLine(
            "----------------------------------------"
        );

        output.WriteLine();
        output.WriteLine(
            "Source: Synthetic subscriptions and backup resources"
        );

        output.WriteLine(
            "External requests: 0"
        );

        output.WriteLine();
        output.WriteLine(
            "TENANT SELECTION"
        );
        output.WriteLine(
            "----------------"
        );

        output.WriteLine(
            $"Acronis customers: {selection.Tenants.Count}"
        );

        output.WriteLine(
            $"Inventory candidates: {selection.InventoryCandidates.Count}"
        );

        output.WriteLine(
            $"Needs scope review: {selection.NeedsReviewCount}"
        );

        output.WriteLine(
            $"Unclassified: {selection.UnclassifiedCount}"
        );

        output.WriteLine(
            $"Unmatched BC observations: {selection.UnmatchedEvidence.Count}"
        );

        output.WriteLine();
        output.WriteLine(
            "INVENTORY EXECUTION"
        );
        output.WriteLine(
            "-------------------"
        );

        output.WriteLine(
            $"Tenants attempted: {result.Inventory.TenantsAttempted}"
        );

        output.WriteLine(
            $"Tenants reviewed: {result.Inventory.TenantCount}"
        );

        output.WriteLine(
            $"Tenant failures: {result.Inventory.FailedTenantCount}"
        );

        output.WriteLine(
            $"Resources checked: {result.Inventory.RowsChecked}"
        );

        output.WriteLine(
            $"Healthy resources: {result.Inventory.HealthyRowsSuppressed}"
        );

        output.WriteLine(
            $"Resources needing review: {result.Inventory.FindingsCount}"
        );

        output.WriteLine();
        output.WriteLine(
            "REVIEWED TENANTS"
        );
        output.WriteLine(
            "----------------"
        );

        foreach (var tenant in result.Inventory.Tenants)
        {
            output.WriteLine(
                $"  {tenant.TenantName}"
            );
        }

        output.WriteLine();
        output.WriteLine(
            "UNRESOLVED MONITORING SCOPE"
        );
        output.WriteLine(
            "---------------------------"
        );

        foreach (var finding in selection.Tenants
            .Where(
                finding =>
                    finding.Status
                    != MonitoringTenantSelectionStatus.CandidateForInventory
            ))
        {
            output.WriteLine(
                $"  {finding.Tenant.Name} | " +
                $"{finding.Status} | {finding.Reason}"
            );
        }

        foreach (var observation in selection.UnmatchedEvidence)
        {
            output.WriteLine(
                $"  Unmatched subscription: " +
                $"{observation.SubscriptionNumber}"
            );
        }

        output.WriteLine();
        output.WriteLine(
            "COMPLETENESS"
        );
        output.WriteLine(
            "------------"
        );

        output.WriteLine(
            $"Selected inventory complete: {YesNo(result.IsInventoryComplete)}"
        );

        output.WriteLine(
            $"Monitoring scope complete: {YesNo(result.IsScopeComplete)}"
        );

        output.WriteLine(
            $"Overall complete: {YesNo(result.IsOverallComplete)}"
        );

        output.WriteLine();
        output.WriteLine(
            "Production BackupAggregator execution and ticketing are unchanged."
        );

        output.WriteLine();

        return 0;
    }

    private static string YesNo(
        bool value)
    {
        return value
            ? "YES"
            : "NO";
    }

    private sealed class NoTenantDiscoveryProvider
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Scoped fixture execution must not enumerate all tenants."
            );
        }
    }

}
