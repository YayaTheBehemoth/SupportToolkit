
namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Evaluates normalized SMARTsubscription evidence.
///
/// The monitoring item identifier is supplied by the caller.
/// This evaluator does not make final operational scope decisions
/// or reconcile Business Central customers with Acronis tenants.
/// </summary>
public sealed class SmartSubscriptionEntitlementEvaluator
{
    private const int MaxReportedStatusLength = 40;

    /// <summary>
    /// First configured item number; kept for single-item callers.
    /// </summary>
    public string MonitoringItemNumber =>
        MonitoringItemNumbers[0];

    /// <summary>
    /// Every item number that confers monitoring entitlement,
    /// normalized for comparison.
    /// </summary>
    public IReadOnlyList<string> MonitoringItemNumbers { get; }

    private readonly HashSet<string> _itemNumberSet;

    /// <summary>
    /// Provisional line-type value that confirms an item line.
    /// Not yet verified against the production representation.
    /// </summary>
    public string ItemLineType { get; }

    /// <summary>
    /// Status texts this evaluator recognizes. The source status is
    /// language dependent, so requests pin the language and only these
    /// exact values are interpreted.
    /// </summary>
    public SmartSubscriptionStatusVocabulary Vocabulary { get; }

    public SmartSubscriptionEntitlementEvaluator(
        string monitoringItemNumber,
        string itemLineType = "Item",
        SmartSubscriptionStatusVocabulary? vocabulary = null)
        : this(
            RequireSingle(monitoringItemNumber),
            itemLineType,
            vocabulary)
    {
    }

    public SmartSubscriptionEntitlementEvaluator(
        IReadOnlyCollection<string> monitoringItemNumbers,
        string itemLineType = "Item",
        SmartSubscriptionStatusVocabulary? vocabulary = null)
    {
        Vocabulary =
            vocabulary
            ?? SmartSubscriptionStatusVocabulary.English;

        ArgumentNullException.ThrowIfNull(monitoringItemNumbers);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            itemLineType
        );

        _itemNumberSet =
            SubscriptionItemNumbers.ToSet(monitoringItemNumbers);

        if (_itemNumberSet.Count == 0
            || monitoringItemNumbers.Any(
                number => string.IsNullOrWhiteSpace(number)))
        {
            throw new ArgumentException(
                "At least one non-blank monitoring item number is required, " +
                "and none may be blank.",
                nameof(monitoringItemNumbers));
        }

        MonitoringItemNumbers =
            monitoringItemNumbers
                .Select(SubscriptionItemNumbers.Normalize)
                .Distinct(SubscriptionItemNumbers.Comparer)
                .ToArray();

        ItemLineType =
            itemLineType.Trim();
    }

    private static string[] RequireSingle(
        string monitoringItemNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            monitoringItemNumber
        );

        return [monitoringItemNumber];
    }

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
                        _itemNumberSet.Contains(
                            line.ItemNumber ?? string.Empty
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

            var lineConcern =
                DescribeLineConcern(group);

            if (lineConcern is not null)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        lineConcern
                    )
                );

                continue;
            }

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

            var itemNumber =
                SubscriptionItemNumbers.Normalize(
                    group.Single().ItemNumber);

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

            var headerConcern =
                DescribeHeaderConcern(header);

            if (headerConcern is not null)
            {
                observations.Add(
                    Review(
                        subscriptionNumber,
                        header.CustomerNumber,
                        headerConcern
                    )
                );

                continue;
            }

            var status =
                header.Status?.Trim();

            if (!string.Equals(
                status,
                Vocabulary.Active,
                StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Only the exact vocabulary values are recognized.
                 * Any other value, including values whose meaning is
                 * unverified, is reported as it was received.
                 */
                var suspended =
                    string.Equals(
                        status,
                        Vocabulary.Suspended,
                        StringComparison.OrdinalIgnoreCase);

                var reason =
                    suspended
                        ? "Subscription is suspended; monitoring responsibility is unverified."
                        : $"Subscription status {DescribeUnknownStatus(status)} " +
                          "is not recognized or confirmed active.";

                observations.Add(
                    new MonitoringEntitlementObservation(
                        subscriptionNumber,
                        header.CustomerNumber,
                        MonitoringEntitlementAssessment.NeedsReview,
                        reason,
                        suspended,
                        itemNumber
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
                    "Active monitoring subscription with applicable dates.",
                    ItemNumber: itemNumber
                )
            );
        }

        return observations.AsReadOnly();
    }

    /// <summary>
    /// Line evidence that prevents a confident monitoring decision.
    /// Lines matching the monitoring item number are never silently
    /// ignored because their type is unconfirmed.
    /// </summary>
    private string? DescribeLineConcern(
        IEnumerable<SmartSubscriptionLine> lines)
    {
        var lineList =
            lines.ToList();

        if (lineList.Any(
                line =>
                    !string.Equals(
                        line.LineType?.Trim(),
                        ItemLineType,
                        StringComparison.OrdinalIgnoreCase
                    )
            ))
        {
            return
                "A line matching the monitoring item number is not " +
                "a confirmed item line.";
        }

        if (lineList.Any(
                line => line.OmitLine != false
            ))
        {
            return
                "A monitoring line is flagged as omitted or does " +
                "not report the omit flag; its meaning is unverified.";
        }

        return null;
    }

    private static string? DescribeHeaderConcern(
        SmartSubscriptionHeader header)
    {
        var blocked =
            header.BlockedValue?.Trim();

        // Provisional: a blank or "false" value is read as not
        // blocked. Anything else, including a missing value, is
        // reviewed rather than interpreted.
        if (blocked is null)
        {
            return
                "Subscription does not report its blocked state.";
        }

        if (blocked.Length > 0
            && !string.Equals(
                blocked,
                "false",
                StringComparison.OrdinalIgnoreCase))
        {
            return
                "Subscription is flagged as blocked or has an " +
                "unrecognized blocked value; monitoring " +
                "responsibility is unverified.";
        }

        var sellTo =
            header.CustomerNumber?.Trim();

        var billTo =
            header.BillToCustomerNumber?.Trim();

        if (!string.IsNullOrEmpty(sellTo)
            && !string.IsNullOrEmpty(billTo)
            && !string.Equals(
                sellTo,
                billTo,
                StringComparison.OrdinalIgnoreCase))
        {
            return
                "Sell-to and bill-to customers differ; the " +
                "monitoring customer is unverified.";
        }

        return null;
    }

    /// <summary>
    /// Reports an unrecognized status explicitly, bounded and
    /// stripped of control characters.
    /// </summary>
    private static string DescribeUnknownStatus(
        string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "(empty)";
        }

        var cleaned =
            new string(
                status
                    .Where(character => !char.IsControl(character))
                    .Take(MaxReportedStatusLength)
                    .ToArray()
            );

        return $"'{cleaned}'";
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
