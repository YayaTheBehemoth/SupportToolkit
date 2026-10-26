using SupportToolkit.Core.CustomerMapping;

namespace SupportToolkit.Modules.BackupAggregator.MonitoringScope;

/// <summary>
/// Supplies identities from Confirmed customer mappings only.
///
/// Suggested, Rejected and Retired mappings are never used. If a
/// tenant or customer occurs in more than one Confirmed mapping, the
/// mapping store is inconsistent and this source fails closed.
///
/// Not wired into production until a mapping storage backend exists.
/// </summary>
public sealed class ConfirmedMappingIdentitySource
    : IMonitoringIdentitySource
{
    private readonly ICustomerMappingRepository _repository;

    public ConfirmedMappingIdentitySource(
        ICustomerMappingRepository repository)
    {
        _repository =
            repository
            ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ResolvedMonitoringCustomerIdentity>>
        GetIdentitiesAsync(
            CancellationToken cancellationToken = default)
    {
        var confirmed =
            (await _repository.GetAllAsync(cancellationToken))
                .Where(
                    mapping =>
                        mapping.State == CustomerMappingState.Confirmed
                )
                .ToArray();

        var duplicated =
            confirmed
                .GroupBy(
                    mapping => mapping.AcronisTenantId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1)
            || confirmed
                .GroupBy(
                    mapping => mapping.BcCustomerNo.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1);

        if (duplicated)
        {
            throw new InvalidOperationException(
                "A tenant or customer occurs in more than one " +
                "confirmed customer mapping, so identities cannot " +
                "be resolved."
            );
        }

        return confirmed
            .Select(
                mapping =>
                    new ResolvedMonitoringCustomerIdentity(
                        mapping.BcCustomerNo.Trim(),
                        mapping.AcronisTenantId.Trim()
                    )
            )
            .ToArray();
    }
}
