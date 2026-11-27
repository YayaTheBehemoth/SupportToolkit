using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>
/// The backup half of the usage report's sources: the backup system's
/// customer tenants and the storage each uses. It comes from the system
/// the profile's backup setting selects, the one the Backup Aggregator
/// reviews.
/// </summary>
public interface IUsageBackupSource : IDisposable
{
    Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken);

    Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken);
}

/// <summary>
/// The commercial half: subscriptions, their billing lines and customer
/// names. It comes from the system the profile's commercial setting
/// selects.
/// </summary>
public interface IUsageCommercialSource : IDisposable
{
    Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken);

    Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken);
}

/// <summary>
/// How the usage report reaches the profile's systems: a local check of
/// each half's configuration (nothing is contacted), how each half is
/// opened, and what the systems are called. Composition/ supplies them
/// from the profile's selections.
/// </summary>
public sealed record UsageReportSystems(
    Func<CancellationToken, Task> CheckBackup,
    Func<CancellationToken, Task> CheckCommercial,
    Func<CancellationToken, Task<IUsageCommercialSource>> OpenCommercial,
    Func<CancellationToken, Task<IUsageBackupSource>> OpenBackup)
{
    /// <summary>
    /// The systems' names for any interface; resolved locally. Null when
    /// they are not known.
    /// </summary>
    public Func<CancellationToken, Task<UsageSystemNames>>? Names { get; init; }
}

/// <summary>
/// What the report's two systems are called, for any interface that shows
/// it, and the few things worth saying about each.
/// </summary>
/// <param name="Backup">The backup system's name.</param>
/// <param name="Billing">The billing system's name.</param>
/// <param name="QuantityName">
/// The billing system's own name for the billed quantity, or null.
/// </param>
/// <param name="GbNote">
/// How the backup system's own screens show GB, or null.
/// </param>
/// <param name="BillingSignsInWithBrowser">
/// True when reading the billing system may open a browser sign-in.
/// </param>
/// <param name="BackupShowsTerabytes">
/// True when the backup system's screens show TB from 1,024 GB on
/// (StorageDisplay), so the report shows figures the same way.
/// </param>
public sealed record UsageSystemNames(
    string Backup,
    string Billing,
    string? QuantityName = null,
    string? GbNote = null,
    bool BillingSignsInWithBrowser = false,
    bool BackupShowsTerabytes = false);

/// <summary>The two halves as the report's one source.</summary>
public sealed class CombinedUsageReportSources : IUsageReportSources
{
    private readonly IUsageCommercialSource _commercial;
    private readonly IUsageBackupSource _backup;

    public CombinedUsageReportSources(
        IUsageCommercialSource commercial,
        IUsageBackupSource backup)
    {
        _commercial = commercial ?? throw new ArgumentNullException(nameof(commercial));
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
    }

    public Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken) =>
        _commercial.GetSubscriptionHeadersAsync(cancellationToken);

    public Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken) =>
        _commercial.GetBillingLinesAsync(cancellationToken);

    public Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken) =>
        _commercial.GetCustomerPartiesAsync(customerNumbers, cancellationToken);

    public Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken) =>
        _backup.GetCustomerTenantsAsync(cancellationToken);

    public Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken) =>
        _backup.GetStorageAsync(tenantIds, cancellationToken);

    public void Dispose()
    {
        _backup.Dispose();
        _commercial.Dispose();
    }
}
