using SupportToolkit.Modules.BackupAggregator.Fixtures;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Supplies resolved relationships between Business Central customers
/// and Acronis tenants.
///
/// An explicitly supplied source decides identities on its own, which
/// fixtures and tests use. Without one, the scoped review resolves
/// identities itself on every run from Business Central customers and
/// Acronis tenants (AutoCustomerMappingResolver), so production needs
/// no source.
/// </summary>
public interface IMonitoringIdentitySource
{
    Task<IReadOnlyList<ResolvedMonitoringCustomerIdentity>>
        GetIdentitiesAsync(
            CancellationToken cancellationToken = default);
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
