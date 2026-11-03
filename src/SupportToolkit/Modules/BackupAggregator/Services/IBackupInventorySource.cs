using SupportToolkit.Core.Tenants;
using SupportToolkit.Modules.BackupAggregator.Models;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Where the backup inventory comes from, as the module's own types. This
/// seam is where provider knowledge stops: an implementation reads a
/// backup system and turns its tenants and resources into tenant records
/// and normalized report entries. The review orchestration behind it does
/// not know which system was read.
/// </summary>
public interface IBackupInventorySource
{
    /// <summary>
    /// The one tenant with exactly this name. Fails when there is none, or
    /// more than one: a tenant is never guessed.
    /// </summary>
    Task<TenantRecord> ResolveExactAsync(
        string tenantName,
        CancellationToken cancellationToken = default);

    /// <summary>Every tenant, whatever its kind.</summary>
    Task<IReadOnlyList<TenantRecord>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The customer tenants only.</summary>
    Task<IReadOnlyList<TenantRecord>> GetCustomerTenantsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and normalizes one tenant's inventory. A failure in a known
    /// stage is reported as a TenantReviewStageException naming the stage.
    /// </summary>
    Task<TenantReviewResult> ReviewTenantAsync(
        TenantRecord tenant,
        CancellationToken cancellationToken = default);
}

/// <summary>One tenant's normalized inventory, with how much was read.</summary>
public sealed record TenantReviewResult(
    TenantBackupReport Report,
    int Microsoft365ResourceCount,
    int DeviceResourceCount
);

/// <summary>
/// A tenant review that failed in a named stage, so the failure can be
/// reported without exposing what the backup system returned.
/// </summary>
public sealed class TenantReviewStageException : Exception
{
    public string Stage { get; }

    public TenantReviewStageException(
        string stage,
        Exception innerException)
        : base($"{stage} failed.", innerException)
    {
        Stage = stage;
    }
}
