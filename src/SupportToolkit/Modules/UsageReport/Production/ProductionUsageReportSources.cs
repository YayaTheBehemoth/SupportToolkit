using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.CustomerMapping;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Modules.UsageReport.Services;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.BusinessCentral;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Modules.UsageReport.Production;

/// <summary>
/// The real systems, read-only: Business Central subscriptions, billing
/// lines and customer names, and Acronis tenants and usage, through the
/// active profile's connections. This folder is the only part of the module
/// that knows Business Central and Acronis exist; it hands the rest of the
/// module its own types.
///
/// Creating the sources never signs in; the Business Central sign-in
/// happens on the first request.
/// </summary>
public sealed class ProductionUsageReportSources : IUsageReportSources
{
    private readonly BusinessCentralProviderBundle _businessCentral;
    private readonly AcronisReadSession _acronis;

    private ProductionUsageReportSources(
        BusinessCentralProviderBundle businessCentral,
        AcronisReadSession acronis)
    {
        _businessCentral = businessCentral;
        _acronis = acronis;
    }

    public static async Task<IUsageReportSources> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // Local configuration first: a missing connection fails before
        // any sign-in or request.
        var businessCentral =
            BusinessCentralProviderFactory.Create(
                await resolver.GetBusinessCentralConnectionAsync(
                    cancellationToken));

        var acronis =
            AcronisReadSession.Create(
                await resolver.GetAcronisConnectionAsync(
                    cancellationToken));

        return new ProductionUsageReportSources(businessCentral, acronis);
    }

    public async Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken)
    {
        var evidence =
            await new BusinessCentralSmartSubscriptionEvidenceSource(
                    _businessCentral.Provider,
                    BusinessCentralSourceContext.From(_businessCentral.Options))
                .GetEvidenceAsync(cancellationToken);

        return evidence.Headers;
    }

    public async Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken)
    {
        var lines =
            await _businessCentral.Provider.GetBillingLinesAsync(cancellationToken);

        return lines
            .OfType<BusinessCentralSubscriptionBillingLineDto>()
            .Select(
                line => new SubscriptionBillingLine(
                    line.SubscriptionNumber,
                    line.LineNumber,
                    line.Number,
                    line.Description,
                    line.UnitOfMeasureCode,
                    line.Quantity,
                    line.UnitPrice,
                    line.LineDiscountPercent,
                    line.Amount,
                    line.OmitLine))
            .ToArray();
    }

    public Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken) =>
        new BusinessCentralCustomerNameSource(_businessCentral.Provider)
            .GetCustomerPartiesAsync(customerNumbers, cancellationToken);

    public async Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken)
    {
        var tenants = await _acronis.Provider.GetTenantsAsync(cancellationToken);

        // Customer tenants only; the operator's own and deleted ones out.
        var parties = TenantCustomerParties.ForResolution(tenants.ToTenantRecords());

        return new UsageTenants(parties.Parties, parties.Notice);
    }

    public async Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken)
    {
        var requested =
            tenantIds.Where(id => Guid.TryParse(id, out _)).ToArray();

        var usage =
            await _acronis.UsageProvider.GetUsagesAsync(requested, cancellationToken);

        return AcronisStorageReader.Read(requested, usage);
    }

    public void Dispose()
    {
        _acronis.Dispose();
    }
}
