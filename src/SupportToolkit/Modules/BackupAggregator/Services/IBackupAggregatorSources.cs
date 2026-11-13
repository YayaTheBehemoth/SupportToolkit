using SupportToolkit.Core.Configuration;
using SupportToolkit.Domain.CustomerMapping;
using SupportToolkit.Domain.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Where the all-tenant review gets its data. The host chooses synthetic
/// data or the real systems for each call; the workflow never decides
/// the mode or reads a connection itself.
/// </summary>
public interface IBackupAggregatorSources
{
    Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken);

    Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken);

    /// <summary>The one tenant with exactly this (trimmed) name.</summary>
    Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Where the monitored (scoped) review gets its data, chosen per call by
/// the host like IBackupAggregatorSources.
/// </summary>
public interface IScopedReviewSources
{
    /// <summary>
    /// The review's mode and whether it can be offered. Any configuration
    /// problem makes it unavailable, with the reason.
    /// </summary>
    Task<ScopedReviewAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Everything one scoped run needs. Local configuration is read
    /// first; nothing here signs in or makes a request.
    /// </summary>
    Task<ScopedReviewInputs> GetInputsAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken);
}

/// <summary>
/// The inputs of one scoped run. An identity source, when given, decides
/// identities before any external read; otherwise they are resolved
/// automatically from the evidence and the tenants, with any stored
/// mappings taking precedence.
/// </summary>
public sealed record ScopedReviewInputs(
    IReadOnlyCollection<string> MonitoringItemNumbers,
    IMonitoringIdentitySource? IdentitySource,
    Func<CancellationToken, Task<ISmartSubscriptionEvidenceSource>> EvidenceSource,
    Func<CancellationToken, Task<ICustomerNameSource>>? NameSource,
    Func<CancellationToken, Task<ScopedInventoryRuntime>> InventoryRuntime,
    IReadOnlySet<string>? ExcludedTenantIds = null,
    ICustomerMappingRepository? MappingRepository = null,
    IReadOnlyCollection<string>? ServiceItemNumbers = null);
