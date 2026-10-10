namespace SupportToolkit.Modules.Ticketing.Models;

/// <summary>
/// Provider-independent representation of a successfully created ticket.
/// </summary>
public sealed record CreatedTicket(
    string Id,
    string? Subject,
    string? Url
);