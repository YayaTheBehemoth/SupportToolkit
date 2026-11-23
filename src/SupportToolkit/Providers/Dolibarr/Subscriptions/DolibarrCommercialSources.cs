using SupportToolkit.Core.Logging;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Transport;

namespace SupportToolkit.Providers.Dolibarr.Subscriptions;

/// <summary>
/// Dolibarr's implementation of the shared subscription-evidence contract.
/// Reads every contract and customer, read-only; the evidence is complete
/// or the read fails.
/// </summary>
public sealed class DolibarrSubscriptionEvidenceSource : ISmartSubscriptionEvidenceSource
{
    private readonly ResolvedDolibarrConnection _connection;
    private readonly Func<HttpClient> _createHttpClient;
    private readonly OperationalLogger? _logger;

    public DolibarrSubscriptionEvidenceSource(
        ResolvedDolibarrConnection connection,
        Func<HttpClient>? createHttpClient = null,
        OperationalLogger? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _createHttpClient = createHttpClient ?? (() => DolibarrHttpPipeline.Create());
        _logger = logger;
    }

    public async Task<SmartSubscriptionEvidence> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        using var httpClient = _createHttpClient();

        var client = new DolibarrApiClient(httpClient, _connection, _logger);

        var contracts = await client.GetContractsAsync(cancellationToken);
        var customers = await client.GetCustomersAsync(cancellationToken);

        return DolibarrSubscriptionMapper.Map(contracts, customers);
    }
}

/// <summary>
/// Dolibarr's implementation of the shared customer-name contract. Like
/// Business Central's: a failed read does not fail the caller; the
/// customers come back without names and the failure is stated, so
/// matching degrades visibly while entitlement is unaffected.
/// </summary>
public sealed class DolibarrCustomerNameSource : ICustomerNameSource
{
    private readonly ResolvedDolibarrConnection _connection;
    private readonly Func<HttpClient> _createHttpClient;
    private readonly OperationalLogger? _logger;

    public DolibarrCustomerNameSource(
        ResolvedDolibarrConnection connection,
        Func<HttpClient>? createHttpClient = null,
        OperationalLogger? logger = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _createHttpClient = createHttpClient ?? (() => DolibarrHttpPipeline.Create());
        _logger = logger;
    }

    public async Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customerNumbers);

        try
        {
            using var httpClient = _createHttpClient();

            var customers =
                await new DolibarrApiClient(httpClient, _connection, _logger)
                    .GetCustomersAsync(cancellationToken);

            return DolibarrSubscriptionMapper.MapCustomers(customerNumbers, customers);
        }
        catch (DolibarrApiException exception)
        {
            return DolibarrSubscriptionMapper.MapCustomers(customerNumbers, []) with
            {
                Failure = exception.Message
            };
        }
    }
}
