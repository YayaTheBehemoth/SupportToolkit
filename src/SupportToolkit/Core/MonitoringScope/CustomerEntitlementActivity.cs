namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Whether a customer's entitlement is currently in force.
/// </summary>
public enum CustomerEntitlementState
{
    /// <summary>At least one subscription is a clean active candidate.</summary>
    Active,

    /// <summary>
    /// Every subscription is plainly inactive: suspended, or blocked
    /// for all. A customer in this state is not expected to have an
    /// Acronis tenant, so the lack of one is no gap.
    /// </summary>
    Inactive,

    /// <summary>
    /// Evidence that is neither clearly active nor clearly inactive.
    /// Never treated as a reason to ignore the customer.
    /// </summary>
    NeedsReview
}

/// <summary>
/// Classifies customers from per-subscription entitlement
/// observations.
///
/// Matching still includes inactive customers, so a tenant that
/// exists for one is found and can be reported as a discrepancy. Only
/// the "no tenant" gap list leaves them out.
/// </summary>
public static class CustomerEntitlementActivity
{
    public static IReadOnlyDictionary<string, CustomerEntitlementState>
        Classify(
            IEnumerable<MonitoringEntitlementObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        return observations
            .Where(
                observation => !string.IsNullOrWhiteSpace(
                    observation.CustomerNumber))
            .GroupBy(
                observation => observation.CustomerNumber!.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => ClassifyCustomer(group),
                StringComparer.OrdinalIgnoreCase);
    }

    private static CustomerEntitlementState ClassifyCustomer(
        IEnumerable<MonitoringEntitlementObservation> observations)
    {
        var list = observations.ToList();

        if (list.Any(
                observation => observation.Assessment
                    == MonitoringEntitlementAssessment.ActiveCandidate))
        {
            return CustomerEntitlementState.Active;
        }

        return list.All(
                observation => observation.IsSuspended
                    || observation.IsBlocked)
            ? CustomerEntitlementState.Inactive
            : CustomerEntitlementState.NeedsReview;
    }
}
