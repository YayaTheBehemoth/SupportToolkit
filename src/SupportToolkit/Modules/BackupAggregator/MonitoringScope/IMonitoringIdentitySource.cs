using SupportToolkit.Modules.BackupAggregator.Fixtures;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Supplies resolved relationships between Business Central customers
/// and Acronis tenants.
///
/// How these relationships are discovered is an open problem. No
/// name-based inference is performed anywhere.
/// </summary>
public interface IMonitoringIdentitySource
{
    Task<IReadOnlyList<ResolvedMonitoringCustomerIdentity>>
        GetIdentitiesAsync(
            CancellationToken cancellationToken = default);
}

/// <summary>
/// Production placeholder until a reliable identity source exists.
///
/// Fails closed before any sign-in or API request, rather than
/// scoping a review with unresolved identities.
/// </summary>
public sealed class UnavailableMonitoringIdentitySource
    : IMonitoringIdentitySource
{
    public Task<IReadOnlyList<ResolvedMonitoringCustomerIdentity>>
        GetIdentitiesAsync(
            CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "No production source is configured for Business Central " +
            "customer to Acronis tenant identities, so a scoped " +
            "review cannot be produced. The unscoped inventory " +
            "review is unchanged."
        );
    }
}

/// <summary>
/// Synthetic identities for fixture execution only.
/// </summary>
public sealed class FixtureMonitoringIdentitySource
    : IMonitoringIdentitySource
{
    public Task<IReadOnlyList<ResolvedMonitoringCustomerIdentity>>
        GetIdentitiesAsync(
            CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            MonitoringTenantSelectionFixtureFactory
                .Create()
                .Identities
        );
    }
}
