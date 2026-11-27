using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Domain.CustomerMapping.Matching;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Modules.UsageReport.Services;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Dolibarr.Subscriptions;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Duplicati.Transport;

namespace SupportToolkit.Modules.UsageReport.Production;

/// <summary>
/// A Duplicati fleet as the usage report's backup system. Each customer is
/// a tenant, named as the Backup Aggregator names it, and its storage is
/// the size of its jobs' backups at their destinations, as each server
/// last reported it (TargetFilesSize).
///
/// - A job that has never completed a backup holds no backup and is not
///   counted; a notice says how many were left out.
/// - A completed job whose size is not reported makes its customer's
///   figure unknown, never zero; so does a server that cannot be read.
/// - A customer with no completed job has nothing reported yet.
/// - Duplicati backs up no Microsoft 365, so that figure is unknown with
///   that reason, never zero.
/// </summary>
public sealed class DuplicatiUsageSource : IUsageBackupSource
{
    public const string NoMicrosoft365 =
        "Duplicati backs up no Microsoft 365";

    public const string Caveat =
        "Duplicati figures are each backup's size at its destination, as " +
        "each server last reported it.";

    private readonly ResolvedDuplicatiFleet _fleet;
    private readonly DuplicatiServerReader _reader;
    private readonly IDisposable? _lifetime;

    public DuplicatiUsageSource(
        ResolvedDuplicatiFleet fleet,
        DuplicatiServerReader reader,
        IDisposable? lifetime = null)
    {
        _fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _lifetime = lifetime;
    }

    /// <summary>
    /// The profile's fleet, resolved with every password before anything
    /// is sent.
    /// </summary>
    public static async Task<IUsageBackupSource> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        var fleet =
            await DuplicatiConnections.Instance.ResolveAsync(resolver, cancellationToken);

        var logger = OperationalLogger.FromEnvironment();
        var httpClient = DuplicatiHttpPipeline.Create();

        return new DuplicatiUsageSource(
            fleet,
            new DuplicatiServerReader(httpClient, logger.DebugEnabled ? logger : null),
            httpClient);
    }

    public Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new UsageTenants(
                _fleet.Customers
                    .Select(customer => new CustomerParty(_fleet.CustomerId(customer), customer.Name))
                    .ToArray(),
                null));

    public async Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        var requested = tenantIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byTenant = new Dictionary<string, TenantStorage>(StringComparer.OrdinalIgnoreCase);
        var neverCompleted = 0;

        foreach (var customer in _fleet.Customers)
        {
            var id = _fleet.CustomerId(customer);

            if (!requested.Contains(id))
            {
                continue;
            }

            var (backup, skipped) = await MeasureAsync(customer, cancellationToken);

            neverCompleted += skipped;

            byTenant[id] =
                new TenantStorage(
                    backup,
                    backup,
                    UsageMeasurement.Unknown(NoMicrosoft365));
        }

        var notices = new List<string> { Caveat };

        if (neverCompleted > 0)
        {
            notices.Add(
                $"{neverCompleted} Duplicati job(s) have never completed a " +
                "backup, so they hold no backup and are not counted.");
        }

        return new UsageStorage(byTenant, null, notices);
    }

    private async Task<(UsageMeasurement Backup, int NeverCompleted)> MeasureAsync(
        ResolvedDuplicatiCustomer customer,
        CancellationToken cancellationToken)
    {
        long total = 0;
        var completed = 0;
        var skipped = 0;

        foreach (var server in customer.Servers)
        {
            IReadOnlyList<DuplicatiJob> jobs;

            try
            {
                jobs = await _reader.ReadJobsAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is HttpRequestException
                          or InvalidOperationException
                          or OperationCanceledException)
            {
                return (
                    UsageMeasurement.Unknown(
                        $"Duplicati server '{server.Alias}' could not be read"),
                    skipped);
            }

            foreach (var job in jobs)
            {
                if (job.LastSuccess is null)
                {
                    skipped++;
                    continue;
                }

                if (job.StoredBytes is not { } bytes)
                {
                    return (
                        UsageMeasurement.Unknown(
                            $"Duplicati reports no size for a job on '{server.Alias}'"),
                        skipped);
                }

                total += bytes;
                completed++;
            }
        }

        return completed == 0
            ? (UsageMeasurement.NothingReported("no completed Duplicati backup"), skipped)
            : (new UsageMeasurement(total, null), skipped);
    }

    public void Dispose()
    {
        _lifetime?.Dispose();
    }
}

/// <summary>
/// Dolibarr as the usage report's billing system: its contracts as
/// subscriptions (numbered as the Backup Aggregator's evidence numbers
/// them), their lines as billing lines, and customer names. Read-only.
/// Dolibarr gives no unit, so the unit is not stated; amounts exclude tax.
/// </summary>
public sealed class DolibarrUsageSource : IUsageCommercialSource
{
    private readonly ResolvedDolibarrConnection _connection;

    public DolibarrUsageSource(ResolvedDolibarrConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public static async Task<IUsageCommercialSource> CreateAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken) =>
        new DolibarrUsageSource(
            await DolibarrConnections.Instance.ResolveAsync(resolver, cancellationToken));

    public async Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken) =>
        (await new DolibarrSubscriptionEvidenceSource(_connection)
            .GetEvidenceAsync(cancellationToken))
        .Headers;

    public async Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken) =>
        (await new DolibarrBillingLineSource(_connection)
            .GetBillingLinesAsync(cancellationToken))
        .Select(line => new SubscriptionBillingLine(
            line.SubscriptionNumber,
            line.LineNumber,
            line.ProductRef,
            line.Description,
            null,
            line.Quantity,
            line.UnitPrice,
            line.DiscountPercent,
            line.Amount,
            false))
        .ToArray();

    public Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken) =>
        new DolibarrCustomerNameSource(_connection)
            .GetCustomerPartiesAsync(customerNumbers, cancellationToken);

    public void Dispose()
    {
        // Each read opens and closes its own client.
    }
}
