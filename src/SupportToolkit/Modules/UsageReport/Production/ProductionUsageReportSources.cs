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
/// The first systems, read-only: Business Central subscriptions, billing
/// lines and customer names, and Acronis tenants and usage, through the
/// active profile's connections. The profile's backup and commercial
/// settings may select other systems (UsageReportSystems); these are what
/// a profile without them reads.
///
/// Creating the sources never signs in; the Business Central sign-in
/// happens on the first request.
/// </summary>
public static class ProductionUsageReportSources
{
    /// <summary>
    /// Acronis and Business Central as the report names them: Business
    /// Central calls the billed quantity "Antal" and may open a browser
    /// sign-in; GB is as the Acronis portal shows it. Also what the
    /// synthetic data stands for.
    /// </summary>
    public static UsageSystemNames FirstSystemNames { get; } =
        new(
            "Acronis",
            "Business Central",
            "Antal",
            "as the Acronis portal shows it",
            true);

    /// <summary>
    /// Business Central, then Acronis: local configuration first, so a
    /// missing connection fails before any sign-in or request.
    /// </summary>
    public static async Task<IUsageReportSources> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var businessCentral =
            await BusinessCentralUsageSource.CreateAsync(resolver, cancellationToken);

        var acronis =
            await AcronisUsageSource.CreateAsync(resolver, cancellationToken);

        return new CombinedUsageReportSources(businessCentral, acronis);
    }

    /// <summary>
    /// The first systems as the report's systems: Acronis for backup and
    /// Business Central for billing, checked and opened as before.
    /// </summary>
    public static UsageReportSystems Systems(
        SupportToolkitConfigurationResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new UsageReportSystems(
            async token => await resolver.GetAcronisConnectionAsync(token),
            async token => await resolver.GetBusinessCentralConnectionAsync(token),
            async token => await BusinessCentralUsageSource.CreateAsync(resolver, token),
            async token => await AcronisUsageSource.CreateAsync(resolver, token))
        {
            Names = _ => Task.FromResult(FirstSystemNames)
        };
    }
}

/// <summary>
/// Business Central as the usage report's billing system: subscriptions,
/// billing lines and customer names, read-only.
/// </summary>
public sealed class BusinessCentralUsageSource : IUsageCommercialSource
{
    private readonly BusinessCentralProviderBundle _businessCentral;

    private BusinessCentralUsageSource(BusinessCentralProviderBundle businessCentral)
    {
        _businessCentral = businessCentral;
    }

    public static async Task<IUsageCommercialSource> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken) =>
        new BusinessCentralUsageSource(
            BusinessCentralProviderFactory.Create(
                await resolver.GetBusinessCentralConnectionAsync(
                    cancellationToken)));

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

    public void Dispose()
    {
        // The Business Central bundle holds nothing to release.
    }
}

/// <summary>
/// Acronis as the usage report's backup system: customer tenants and their
/// storage, read-only.
/// </summary>
public sealed class AcronisUsageSource : IUsageBackupSource
{
    private readonly AcronisReadSession _acronis;

    private AcronisUsageSource(AcronisReadSession acronis)
    {
        _acronis = acronis;
    }

    public static async Task<IUsageBackupSource> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken) =>
        new AcronisUsageSource(
            AcronisReadSession.Create(
                await resolver.GetAcronisConnectionAsync(
                    cancellationToken)));

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
