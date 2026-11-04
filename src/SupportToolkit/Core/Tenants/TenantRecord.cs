namespace SupportToolkit.Core.Tenants;

/// <summary>
/// What a tenant is to the operator, as the provider classified it.
/// </summary>
public enum TenantRole
{
    /// <summary>Anything else (a unit, a folder, an unknown kind).</summary>
    Other,

    /// <summary>A customer of the operator.</summary>
    Customer,

    /// <summary>The operator's own partner (reseller) tenant.</summary>
    Partner
}

/// <summary>
/// A tenant in the backup system, with only what the toolkit's rules use.
///
/// Modules decide on this type, never on a provider's own tenant DTO, so
/// their rules do not depend on which system the tenants came from. A
/// provider adapter converts its tenants to this type at the edge and
/// classifies them there: the rules read Role and CanonicalId, never the
/// provider's own kind names or identifier format.
/// </summary>
public sealed class TenantRecord
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The kind as the system states it, for display only.</summary>
    public string? Kind { get; init; }

    /// <summary>What the tenant is to the operator, set by the provider.</summary>
    public TenantRole Role { get; init; }

    /// <summary>
    /// The identifier in its canonical form, when it is one the system can
    /// issue; null when it is not. A tenant without one is never guessed.
    /// </summary>
    public string? CanonicalId { get; init; }

    /// <summary>Set when the tenant has been deleted.</summary>
    public DateTimeOffset? DeletedAt { get; init; }

    public bool? Enabled { get; init; }
}
