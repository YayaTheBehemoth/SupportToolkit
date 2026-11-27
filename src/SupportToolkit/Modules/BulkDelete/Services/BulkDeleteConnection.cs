using SupportToolkit.Modules.BulkDelete.Models;

namespace SupportToolkit.Modules.BulkDelete.Services;

/// <summary>
/// Whether the active profile can be used for bulk deletion. Instance is
/// the ticketing instance's name when it can (for Zendesk, the subdomain);
/// Reason says why when not.
/// </summary>
public sealed record BulkDeleteAvailability(
    bool IsAvailable,
    string? Instance,
    string? Reason)
{
    /// <summary>
    /// The rules of the ticketing system bulk delete works on, when the
    /// profile's ticketing system is one SupportToolkit can delete from.
    /// </summary>
    public TicketSystemRules? Rules { get; init; }

    /// <summary>
    /// The profile's ticketing system as operators know it, when known,
    /// including one SupportToolkit cannot delete from.
    /// </summary>
    public string? SystemName { get; init; }

    /// <summary>The instance as operators know it ("example.zendesk.com").</summary>
    public string? DisplayInstance =>
        Instance is not null && Rules is not null
            ? Rules.DisplayInstance(Instance)
            : Instance;
}

/// <summary>
/// A bulk-delete review session and whatever must be disposed with it.
/// </summary>
public sealed record BulkDeleteConnection(
    BulkDeleteReviewSession Session,
    IDisposable? Lifetime)
{
    /// <summary>The rules of the system this session deletes from.</summary>
    public TicketSystemRules? Rules { get; init; }
}
