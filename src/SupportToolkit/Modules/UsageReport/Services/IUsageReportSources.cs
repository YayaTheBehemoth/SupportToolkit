using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>
/// Everything a usage report reads, as the module's own types. This seam
/// is where provider knowledge stops: an implementation turns Business
/// Central and Acronis data (or synthetic data) into these types, and
/// nothing behind it knows which systems were read. Every read is
/// read-only.
/// </summary>
public interface IUsageReportSources : IDisposable
{
    Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken);

    Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken);

    Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken);

    Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken);
}
