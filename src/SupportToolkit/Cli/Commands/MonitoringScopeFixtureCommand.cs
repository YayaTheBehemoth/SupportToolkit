
using System.Globalization;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Fixture-only investigation report for monitoring entitlement
/// and hypothetical Acronis tenant selection.
///
/// No external provider access, production filtering or ticketing.
/// </summary>
public sealed class MonitoringScopeFixtureCommand
{
    public int Run(
        TextWriter output,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(output);

        var fixture =
            SmartSubscriptionFixtureFactory.Create();

        var monitoringItem =
            SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber;

        var evaluator =
            new SmartSubscriptionEntitlementEvaluator(
                monitoringItem
            );

        var observations =
            evaluator.Evaluate(
                fixture.Headers,
                fixture.Lines,
                asOfDate
            );

        var activeCandidates =
            observations
                .Where(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.ActiveCandidate
                )
                .ToList();

        var needsReview =
            observations
                .Where(
                    observation =>
                        observation.Assessment
                        == MonitoringEntitlementAssessment.NeedsReview
                )
                .ToList();

        var observedSubscriptionNumbers =
            observations
                .Select(
                    observation =>
                        observation.SubscriptionNumber
                )
                .ToHashSet(
                    StringComparer.OrdinalIgnoreCase
                );

        var headersWithoutMonitoringLine =
            fixture.Headers
                .Where(
                    header =>
                        !observedSubscriptionNumbers.Contains(
                            header.SubscriptionNumber.Trim()
                        )
                )
                .ToList();

        output.WriteLine();
        output.WriteLine(
            "MONITORING SCOPE - FIXTURE INVESTIGATION"
        );
        output.WriteLine(
            "----------------------------------------"
        );
        output.WriteLine();

        output.WriteLine(
            "Source: Synthetic SMARTsubscription records"
        );

        output.WriteLine(
            "Evaluation date: " +
            asOfDate.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            )
        );

        output.WriteLine(
            "Monitoring item: " + monitoringItem
        );

        output.WriteLine();
        output.WriteLine("EVIDENCE SUMMARY");
        output.WriteLine("----------------");

        output.WriteLine(
            $"Subscription headers: {fixture.Headers.Count}"
        );

        output.WriteLine(
            $"Subscription lines: {fixture.Lines.Count}"
        );

        output.WriteLine(
            "Subscriptions with monitoring evidence: " +
            observations.Count
        );

        output.WriteLine(
            $"Active candidates: {activeCandidates.Count}"
        );

        output.WriteLine(
            $"Needs review: {needsReview.Count}"
        );

        output.WriteLine(
            "Headers without monitoring line: " +
            headersWithoutMonitoringLine.Count
        );

        output.WriteLine();
        output.WriteLine("ACTIVE MONITORING CANDIDATES");
        output.WriteLine("----------------------------");

        WriteObservations(
            output,
            activeCandidates
        );

        output.WriteLine();
        output.WriteLine("REQUIRES INVESTIGATION");
        output.WriteLine("----------------------");

        WriteObservations(
            output,
            needsReview
        );

        output.WriteLine();
        output.WriteLine("HEADERS WITHOUT MONITORING EVIDENCE");
        output.WriteLine("-----------------------------------");

        if (headersWithoutMonitoringLine.Count == 0)
        {
            output.WriteLine("  None.");
        }
        else
        {
            foreach (var header in headersWithoutMonitoringLine
                .OrderBy(
                    header => header.SubscriptionNumber,
                    StringComparer.OrdinalIgnoreCase
                ))
            {
                output.WriteLine(
                    $"  {header.SubscriptionNumber} | " +
                    $"Customer: {header.CustomerNumber ?? "<missing>"}"
                );
            }
        }

        /*
         * Tenant-selection simulation:
         * the identities below are entirely synthetic.
         *
         * This does not modify any BackupAggregator inventory
         * collection or production tenant-selection logic.
         */
        var acronisFixture =
            MonitoringTenantSelectionFixtureFactory.Create();

        var selection =
            new MonitoringTenantSelector()
                .Select(
                    acronisFixture.Tenants,
                    observations,
                    acronisFixture.Identities
                );

        output.WriteLine();
        output.WriteLine(
            "TENANT SELECTION - SYNTHETIC SIMULATION"
        );
        output.WriteLine(
            "---------------------------------------"
        );

        output.WriteLine(
            $"Acronis customer tenants: {selection.Tenants.Count}"
        );

        output.WriteLine(
            $"Inventory candidates: {selection.InventoryCandidates.Count}"
        );

        output.WriteLine(
            $"Needs review: {selection.NeedsReviewCount}"
        );

        output.WriteLine(
            $"Unclassified: {selection.UnclassifiedCount}"
        );

        output.WriteLine(
            $"Unmatched BC observations: {selection.UnmatchedEvidence.Count}"
        );

        output.WriteLine();
        output.WriteLine("CANDIDATES FOR INVENTORY");
        output.WriteLine("------------------------");

        foreach (var tenant in selection.InventoryCandidates)
        {
            output.WriteLine(
                $"  {tenant.Name}"
            );
        }

        output.WriteLine();
        output.WriteLine("TENANTS REQUIRING SCOPE RESOLUTION");
        output.WriteLine("----------------------------------");

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

        if (selection.UnmatchedEvidence.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(
                "BC OBSERVATIONS WITHOUT A MATCHED ACRONIS TENANT"
            );
            output.WriteLine(
                "-----------------------------------------------"
            );

            foreach (var observation in selection.UnmatchedEvidence)
            {
                output.WriteLine(
                    $"  {observation.SubscriptionNumber} | " +
                    $"{observation.Reason}"
                );
            }
        }

        output.WriteLine();
        output.WriteLine("IMPORTANT");
        output.WriteLine("---------");

        output.WriteLine(
            "Active candidates are not confirmed monitoring assignments."
        );

        output.WriteLine(
            "Missing monitoring lines do not establish non-entitlement."
        );

        output.WriteLine(
            "BC-to-Acronis relationships in this simulation are fictional."
        );

        output.WriteLine(
            "No production BC-to-Acronis identity reconciliation exists."
        );

        output.WriteLine(
            "BackupAggregator production behavior is unchanged."
        );

        output.WriteLine();

        return 0;
    }

    private static void WriteObservations(
        TextWriter output,
        IReadOnlyList<MonitoringEntitlementObservation> observations)
    {
        if (observations.Count == 0)
        {
            output.WriteLine("  None.");
            return;
        }

        foreach (var observation in observations
            .OrderBy(
                observation => observation.SubscriptionNumber,
                StringComparer.OrdinalIgnoreCase
            ))
        {
            var subscription =
                string.IsNullOrWhiteSpace(
                    observation.SubscriptionNumber
                )
                    ? "<missing>"
                    : observation.SubscriptionNumber;

            var customer =
                string.IsNullOrWhiteSpace(
                    observation.CustomerNumber
                )
                    ? "<missing>"
                    : observation.CustomerNumber;

            output.WriteLine();

            output.WriteLine(
                $"  Subscription: {subscription}"
            );

            output.WriteLine(
                $"  Customer:     {customer}"
            );

            output.WriteLine(
                $"  Finding:      {observation.Reason}"
            );
        }
    }
}
