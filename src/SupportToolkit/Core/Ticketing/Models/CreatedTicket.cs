
namespace SupportToolkit.Core.Ticketing.Models;

/// <summary>
/// Provider-independent representation of a ticket confirmed by
/// an external ticketing system.
///
/// WasIdempotentReplay:
///   true  = the provider explicitly confirmed a replay.
///   false = the provider explicitly confirmed a new creation.
///   null  = the provider did not report the distinction.
/// </summary>
public sealed record CreatedTicket(
    string Id,
    string? Subject,
    string? Url)
{
    public bool? WasIdempotentReplay { get; init; }

    /// <summary>
    /// User-facing link to open the ticket in the provider's UI.
    /// Url remains the original API URL for compatibility.
    /// </summary>
    public string? ViewUrl { get; init; }
}
