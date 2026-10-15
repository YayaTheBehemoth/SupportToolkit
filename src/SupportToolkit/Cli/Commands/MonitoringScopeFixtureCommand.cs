
using System.Globalization;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Cli.Commands;

/// <summary>
/// Fixture-only investigation report for normalized
/// SMARTsubscription monitoring entitlement evidence.
///
/// No external provider access or operational scope filtering.
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
        output.WriteLine(
            "EVIDENCE SUMMARY"
        );
        output.WriteLine(
            "----------------"
        );

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
        output.WriteLine(
            "ACTIVE MONITORING CANDIDATES"
        );
        output.WriteLine(
            "----------------------------"
        );

        WriteObservations(
            output,
            activeCandidates
        );

        output.WriteLine();
        output.WriteLine(
            "REQUIRES INVESTIGATION"
        );
        output.WriteLine(
            "----------------------"
        );

        WriteObservations(
            output,
            needsReview
        );

        output.WriteLine();
        output.WriteLine(
            "HEADERS WITHOUT MONITORING EVIDENCE"
        );
        output.WriteLine(
            "-----------------------------------"
        );

        if (headersWithoutMonitoringLine.Count == 0)
        {
            output.WriteLine(
                "  None."
            );
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

        output.WriteLine();
        output.WriteLine(
            "IMPORTANT"
        );
        output.WriteLine(
            "---------"
        );

        output.WriteLine(
            "Active candidates are not confirmed monitoring assignments."
        );

        output.WriteLine(
            "Missing monitoring lines do not establish non-entitlement."
        );

        output.WriteLine(
            "No BC-to-Acronis identity reconciliation has been performed."
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
            output.WriteLine(
                "  None."
            );

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
