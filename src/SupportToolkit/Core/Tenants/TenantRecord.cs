namespace SupportToolkit.Core.Tenants;

/// <summary>
/// A tenant in the backup system, with only what the toolkit's rules use.
///
/// Modules decide on this type, never on a provider's own tenant DTO, so
/// their rules do not depend on which system the tenants came from. A
/// provider adapter converts its tenants to this type at the edge.
/// </summary>
public sealed class TenantRecord
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>For example "customer" or "partner", as the system states it.</summary>
    public string? Kind { get; init; }

    /// <summary>Set when the tenant has been deleted.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    public bool? Enabled { get; init; }
}
