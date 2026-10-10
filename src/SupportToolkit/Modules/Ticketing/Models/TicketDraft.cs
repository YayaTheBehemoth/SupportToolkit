namespace SupportToolkit.Modules.Ticketing.Models;

/// <summary>
/// Provider-independent representation of a ticket that SupportToolkit
/// wants to create.
///
/// External ticketing providers are responsible for translating this model
/// into their own API-specific request format.
/// </summary>
public sealed class TicketDraft
{
    public required string Subject { get; init; }

    public required string Body { get; init; }
}