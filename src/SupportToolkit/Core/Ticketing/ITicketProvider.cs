using SupportToolkit.Core.Ticketing.Models;

namespace SupportToolkit.Core.Ticketing;

/// <summary>
/// Provider-independent contract for creating tickets in an external
/// ticketing system.
/// </summary>
public interface ITicketProvider
{
    Task<CreatedTicket> CreateTicketAsync(
        TicketDraft draft,
        string idempotencyKey,
        CancellationToken cancellationToken = default
    );
}