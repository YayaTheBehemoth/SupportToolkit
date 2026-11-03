using SupportToolkit.Core.CustomerMapping.Matching;

namespace SupportToolkit.Core.MonitoringScope;

/// <summary>
/// Customers in scope as matching evidence, with counts that say how
/// trustworthy the names are.
///
/// Failure is set when the names could not be read at all. The
/// customers are still listed, without names, so a caller can report
/// them and degrade matching instead of losing the scope.
/// </summary>
public sealed record ScopedCustomerParties(
    IReadOnlyList<CustomerParty> Parties,
    int CustomersWithoutName,
    int CustomersWithConflictingNames,
    string? Failure = null)
{
    public int CustomersWithName =>
        Parties.Count - CustomersWithoutName;
}

/// <summary>
/// Shared monitoring-scope capability: supplies customer names for
/// the customers a module has scoped.
///
/// Separate from the subscription evidence on purpose. A module that
/// only needs entitlement never depends on names, and a failed name
/// read affects matching only, never the entitlement decision.
/// </summary>
public interface ICustomerNameSource
{
    Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken = default);
}
