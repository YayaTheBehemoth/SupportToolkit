using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Domain.Tenants;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Jobs;
using SupportToolkit.Providers.Duplicati.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Production;

/// <summary>
/// A Duplicati fleet as the review's tenants and resources: each customer
/// is a tenant, and each backup job on its servers is a resource.
/// Classification follows the Acronis rules: a running job is healthy only
/// once it has succeeded before, and anything undecided stays visible.
/// </summary>
public sealed class DuplicatiBackupInventorySource : IBackupInventorySource
{
    private readonly ResolvedDuplicatiFleet _fleet;
    private readonly DuplicatiServerReader _reader;

    public DuplicatiBackupInventorySource(
        ResolvedDuplicatiFleet fleet,
        DuplicatiServerReader reader)
    {
        _fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public string SystemName => "Duplicati";

    public Task<TenantRecord> ResolveExactAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        var name = tenantName?.Trim() ?? string.Empty;

        var matches =
            _fleet.Customers
                .Where(customer => string.Equals(customer.Name, name, StringComparison.Ordinal))
                .ToArray();

        return matches.Length switch
        {
            1 => Task.FromResult(Tenant(matches[0])),
            0 => throw new InvalidOperationException(
                $"No Duplicati customer is named exactly '{name}'."),
            _ => throw new InvalidOperationException(
                $"More than one Duplicati customer is named '{name}'.")
        };
    }

    public Task<IReadOnlyList<TenantRecord>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantRecord>>(
            _fleet.Customers.Select(Tenant).ToArray());

    /// <summary>Every tenant of a fleet is a customer.</summary>
    public Task<IReadOnlyList<TenantRecord>> GetCustomerTenantsAsync(
        CancellationToken cancellationToken = default) =>
        GetAllTenantsAsync(cancellationToken);

    public async Task<TenantReviewResult> ReviewTenantAsync(
        TenantRecord tenant,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var customer =
            _fleet.Customers.SingleOrDefault(candidate => Tenant(candidate).Id == tenant.Id)
            ?? throw new InvalidOperationException(
                "The tenant is not a customer of this Duplicati fleet.");

        var entries = new List<BackupReportEntry>();

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
                throw new TenantReviewStageException(
                    $"Reading Duplicati server '{server.Alias}'",
                    exception);
            }

            entries.AddRange(jobs.Select(job => Entry(server.Alias, job)));
        }

        return new TenantReviewResult(
            new TenantBackupReport
            {
                TenantName = customer.Name,
                Entries = entries
            },
            [new ResourceCount("Duplicati jobs", entries.Count)]);
    }

    /// <summary>
    /// The fleet's identity of the customer (the monitored review requires
    /// a canonical one).
    /// </summary>
    private TenantRecord Tenant(ResolvedDuplicatiCustomer customer)
    {
        var id = _fleet.CustomerId(customer);

        return new()
        {
            Id = id,
            CanonicalId = id,
            Name = customer.Name,
            Kind = "customer",
            Role = TenantRole.Customer,
            Enabled = true
        };
    }

    internal static BackupReportEntry Entry(string serverAlias, DuplicatiJob job) =>
        new()
        {
            ResourceName = $"{serverAlias}: {job.Name}",
            PlanName = job.Name,
            LastResult = job.Outcome.ToString(),
            Classification = Classify(job),
            LastBackupRun = job.LastRun,
            LastSuccessfulBackup = job.LastSuccess,
            ResourceState = job.IsRunning ? "running" : null,
            DisplayStatus = job.IsRunning
                ? "In progress"
                : job.Outcome switch
                {
                    DuplicatiJobOutcome.Success => "Successful",
                    DuplicatiJobOutcome.Warning => "Completed with warnings",
                    DuplicatiJobOutcome.Failed => "Failed",
                    DuplicatiJobOutcome.NeverRun => "Never run",
                    _ => "Unknown"
                },
            UnclassifiedReason = job.Detail ?? "Duplicati reported no usable result"
        };

    private static BackupReportEntryClassification Classify(DuplicatiJob job)
    {
        if (job.IsRunning)
        {
            return job.LastSuccess is not null
                ? BackupReportEntryClassification.Healthy
                : BackupReportEntryClassification.NeedsReview;
        }

        return job.Outcome switch
        {
            DuplicatiJobOutcome.Success => BackupReportEntryClassification.Healthy,
            DuplicatiJobOutcome.Unknown => BackupReportEntryClassification.Unknown,
            _ => BackupReportEntryClassification.NeedsReview
        };
    }
}

/// <summary>
/// The review runtime for a Duplicati fleet: configuration is resolved,
/// with every password, before anything is sent.
/// </summary>
public static class DuplicatiBackupAggregator
{
    public static async Task<ScopedInventoryRuntime> CreateRuntimeAsync(
        SupportToolkitConfigurationResolver resolver,
        ReviewRunMode runMode,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var fleet =
            await DuplicatiConnections.Instance.ResolveAsync(resolver, cancellationToken);

        var wait =
            await InProgressWaitSetup.LoadAsync(
                resolver,
                BackupAggregatorModule.ModuleCommand,
                runMode,
                cancellationToken);

        var logger = OperationalLogger.FromEnvironment();
        var httpClient = DuplicatiHttpPipeline.Create();

        var reviewService =
            new BackupInventoryReviewService(
                new DuplicatiBackupInventorySource(
                    fleet,
                    new DuplicatiServerReader(
                        httpClient,
                        logger.DebugEnabled ? logger : null)),
                logger,
                inProgressWait: wait,
                progress: progress);

        return new ScopedInventoryRuntime(reviewService, httpClient);
    }
}
