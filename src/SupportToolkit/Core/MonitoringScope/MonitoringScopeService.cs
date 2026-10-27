namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Who is in scope for one module's service item(s) on one date.
///
/// Customers holds the state of every customer with a line for an
/// entitling item. ServiceCustomerNumbers is wider: every customer
/// with a line for an entitling item or a service item, entitled or
/// not. Evidence is carried so a module can reconcile against the
/// same subscriptions the scope was decided from.
/// </summary>
public sealed record MonitoringScopeResult(
    IReadOnlyList<MonitoringEntitlementObservation> Observations,
    IReadOnlyDictionary<string, CustomerEntitlementState> Customers,
    SmartSubscriptionEvidence Evidence,
    IReadOnlyList<string> ServiceCustomerNumbers)
{
    public IReadOnlyList<string> CustomerNumbers =>
        Customers.Keys
            .OrderBy(number => number, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Customers that hold an entitling item but none of the service
    /// items. Empty unless service items were supplied. For a backup
    /// monitoring add-on this is an inconsistency: the add-on implies
    /// the backup itself.
    /// </summary>
    public IReadOnlyList<string> EntitledWithoutServiceItem { get; init; } =
        [];

    public CustomerEntitlementState StateOf(
        string customerNumber)
    {
        return Customers.TryGetValue(
            customerNumber.Trim(),
            out var state)
                ? state
                : CustomerEntitlementState.NeedsReview;
    }
}

/// <summary>
/// The shared entry point for scoping a module's work to entitled
/// customers.
///
/// A module supplies its own item numbers; this service reads the
/// evidence and decides, once, what "in scope" means, so no module
/// re-implements it. What the result narrows (Acronis tenants,
/// another system's targets) stays with the module.
/// </summary>
public sealed class MonitoringScopeService
{
    private readonly ISmartSubscriptionEvidenceSource _evidenceSource;

    public MonitoringScopeService(
        ISmartSubscriptionEvidenceSource evidenceSource)
    {
        _evidenceSource =
            evidenceSource
            ?? throw new ArgumentNullException(nameof(evidenceSource));
    }

    /// <param name="itemNumbers">
    /// The module's entitling items. They decide entitlement.
    /// </param>
    /// <param name="serviceItemNumbers">
    /// Optional items that show a customer has the underlying service.
    /// They never entitle anyone; they only widen
    /// ServiceCustomerNumbers.
    /// </param>
    public async Task<MonitoringScopeResult> ResolveAsync(
        IReadOnlyCollection<string> itemNumbers,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? serviceItemNumbers = null)
    {
        ArgumentNullException.ThrowIfNull(itemNumbers);

        var evidence =
            await _evidenceSource.GetEvidenceAsync(cancellationToken);

        var observations =
            new SmartSubscriptionEntitlementEvaluator(itemNumbers)
                .Evaluate(
                    evidence.Headers,
                    evidence.Lines,
                    asOfDate);

        var customers =
            CustomerEntitlementActivity.Classify(observations);

        var serviceCustomers =
            customers.Keys.ToList();

        var entitledWithoutService =
            new List<string>();

        if (serviceItemNumbers is { Count: > 0 })
        {
            var serviceNumbers =
                CustomersWithItems(
                    evidence,
                    serviceItemNumbers.Concat(itemNumbers).ToArray(),
                    asOfDate);

            var withServiceItem =
                CustomersWithItems(
                    evidence,
                    serviceItemNumbers,
                    asOfDate);

            serviceCustomers =
                customers.Keys
                    .Concat(serviceNumbers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            entitledWithoutService =
                customers.Keys
                    .Where(number => !withServiceItem.Contains(number))
                    .OrderBy(number => number, StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        return new MonitoringScopeResult(
            observations,
            customers,
            evidence,
            serviceCustomers
                .OrderBy(number => number, StringComparer.OrdinalIgnoreCase)
                .ToArray())
        {
            EntitledWithoutServiceItem = entitledWithoutService
        };
    }

    private static HashSet<string> CustomersWithItems(
        SmartSubscriptionEvidence evidence,
        IReadOnlyCollection<string> items,
        DateOnly asOfDate)
    {
        return new SmartSubscriptionEntitlementEvaluator(items)
            .Evaluate(evidence.Headers, evidence.Lines, asOfDate)
            .Select(observation => observation.CustomerNumber)
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .Select(number => number!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
