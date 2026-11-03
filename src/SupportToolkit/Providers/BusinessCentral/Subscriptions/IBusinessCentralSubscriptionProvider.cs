using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Raw subscription evidence read from Business Central.
///
/// Header and line collections are complete or the read failed:
/// providers never return a partial collection.
/// </summary>
public sealed record BusinessCentralSubscriptionData(
    IReadOnlyList<BusinessCentralSubscriptionHeaderDto?> Headers,
    IReadOnlyList<BusinessCentralSubscriptionLineDto?> Lines
);

/// <summary>
/// Module-independent access to Business Central subscription
/// evidence.
///
/// Implementations return provider DTOs only. Interpretation,
/// mapping and entitlement decisions belong to the consuming module.
/// </summary>
public interface IBusinessCentralSubscriptionProvider
{
    Task<BusinessCentralSubscriptionData> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Customer names carried on subscription header rows. A separate
/// contract from the subscription data so that a renamed or missing
/// name column fails this read alone.
/// </summary>
public interface IBusinessCentralCustomerNameProvider
{
    Task<IReadOnlyList<BusinessCentralHeaderCustomerNameDto?>>
        GetHeaderCustomerNamesAsync(
            CancellationToken cancellationToken = default);
}

/// <summary>
/// What subscription lines are billed at. A separate contract from the
/// subscription data, so that a renamed or missing price column fails
/// this read alone and never the entitlement read.
/// </summary>
public interface IBusinessCentralSubscriptionBillingProvider
{
    Task<IReadOnlyList<BusinessCentralSubscriptionBillingLineDto?>>
        GetBillingLinesAsync(
            CancellationToken cancellationToken = default);
}
