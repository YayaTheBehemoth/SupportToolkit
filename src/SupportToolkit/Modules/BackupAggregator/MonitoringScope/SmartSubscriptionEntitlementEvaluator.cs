
namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Evaluates synthetic or normalized SMARTsubscription evidence.
///
/// The evaluator identifies candidate monitoring entitlements,
/// but does not make final operational scope decisions.
///
/// Missing and contradictory evidence remains visible.
/// </summary>
public sealed class SmartSubscriptionEntitlementEvaluator
{
    public const string MonitoringItemNumber =
        "BAASOVERVÅG";

    public IReadOnlyList<MonitoringEntitlementObservation> Evaluate(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(lines);

        var headersByNumber =
            headers
                .Where(
                    header =>
                        !string.IsNullOrWhiteSpace(
                            header.SubscriptionNumber
                        )
                )
                .GroupBy(
                    header => header.SubscriptionNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList(),
                    StringComparer.OrdinalIgnoreCase
                );

        var monitoringLines =
            lines
                .Where(
                    line =>
                        string.Equals(
                            line.ItemNumber?.Trim(),
                            MonitoringItemNumber,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .GroupBy(
                    line =>
                        line.SubscriptionNumber?.Trim()
                        ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase
                )
                .OrderBy(
                    group => group.Key,
                    StringComparer.OrdinalIgnoreCase
                );

        var observations =
            new List<MonitoringEntitlementObservation>();

        foreach (var group in monitoringLines)
        {
            var subscriptionNumber =
                group.Key;

            if (string.IsNullOrWhiteSpace(
                subscriptionNumber))
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        null,
                        "Monitoring line has no subscription reference."
                    )
                );

                continue;
            }

            if (!headersByNumber.TryGetValue(
                subscriptionNumber,
                out var matchingHeaders))
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        null,
                        "Monitoring line has no matching subscription header."
                    )
                );

                continue;
            }

            if (matchingHeaders.Count != 1)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        null,
                        "Multiple subscription headers share the same reference."
                    )
                );

                continue;
            }

            var header =
                matchingHeaders[0];

            if (group.Count() != 1)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        "Multiple monitoring lines require investigation."
                    )
                );

                continue;
            }

            if (string.IsNullOrWhiteSpace(
                header.CustomerNumber))
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        null,
                        "Subscription has no customer reference."
                    )
                );

                continue;
            }

            if (!string.Equals(
                header.Status?.Trim(),
                "Aktiv",
                StringComparison.OrdinalIgnoreCase))
            {
                var reason =
                    header.Status?.Trim() switch
                    {
                        "Spærret" =>
                            "Subscription is blocked; monitoring responsibility is unverified.",

                        "Planlægges" =>
                            "Subscription is planned, not confirmed active.",

                        _ =>
                            "Subscription status is not confirmed active."
                    };

                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        reason
                    )
                );

                continue;
            }

            if (!header.StartDate.HasValue)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        "Active subscription has no start date."
                    )
                );

                continue;
            }

            if (header.EndDate.HasValue
                && header.EndDate.Value < header.StartDate.Value)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        "Subscription end date precedes its start date."
                    )
                );

                continue;
            }

            if (header.StartDate.Value > asOfDate)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        "Subscription start date is in the future."
                    )
                );

                continue;
            }

            if (header.EndDate.HasValue
                && header.EndDate.Value < asOfDate)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        "Subscription end date has passed."
                    )
                );

                continue;
            }

            observations.Add(
                new MonitoringEntitlementObservation(
                    subscriptionNumber,
                    header.CustomerNumber,
                    MonitoringEntitlementAssessment.ActiveCandidate,
                    "Active monitoring subscription with applicable dates."
                )
            );
        }

        return observations.AsReadOnly();
    }

    private static MonitoringEntitlementObservation Review(
        string subscriptionNumber,
        string? customerNumber,
        string reason)
    {
        return new MonitoringEntitlementObservation(
            subscriptionNumber,
            customerNumber,
            MonitoringEntitlementAssessment.NeedsReview,
            reason
        );
    }
}
