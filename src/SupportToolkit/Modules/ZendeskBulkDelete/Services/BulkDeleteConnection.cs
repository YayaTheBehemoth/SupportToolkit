namespace SupportToolkit.Modules.ZendeskBulkDelete.Services;

/// <summary>
/// Whether the active profile can be used for bulk deletion. Instance is
/// the ticketing instance's name when it can; Reason says why when not.
/// </summary>
public sealed record BulkDeleteAvailability(
    bool IsAvailable,
    string? Subdomain,
    string? Reason);

/// <summary>
/// A bulk-delete review session and whatever must be disposed with it.
/// </summary>
public sealed record BulkDeleteConnection(
    BulkDeleteReviewSession Session,
    IDisposable? Lifetime);
