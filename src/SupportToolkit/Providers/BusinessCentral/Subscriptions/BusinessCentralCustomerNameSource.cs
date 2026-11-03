using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Business Central implementation of the shared customer-name
/// contract.
///
/// An API failure on the name read does not fail the caller: the
/// customers come back without names and with the failure stated, so
/// matching degrades visibly while the entitlement decision is
/// unaffected. Contract violations and anything unexpected still
/// propagate.
/// </summary>
public sealed class BusinessCentralCustomerNameSource
    : ICustomerNameSource
{
    private readonly IBusinessCentralCustomerNameProvider _provider;

    public BusinessCentralCustomerNameSource(
        IBusinessCentralCustomerNameProvider provider)
    {
        _provider =
            provider
            ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerNumbers);

        try
        {
            var rows =
                await _provider.GetHeaderCustomerNamesAsync(
                    cancellationToken);

            return BusinessCentralCustomerPartyMapper.Map(
                customerNumbers,
                rows);
        }
        catch (BusinessCentralApiException exception)
        {
            var withoutNames =
                BusinessCentralCustomerPartyMapper.Map(
                    customerNumbers,
                    []);

            return withoutNames with
            {
                Failure = exception.Message
            };
        }
    }
}
