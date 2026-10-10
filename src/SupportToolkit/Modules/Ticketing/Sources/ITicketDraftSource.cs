using SupportToolkit.Modules.Ticketing.Models;

namespace SupportToolkit.Modules.Ticketing.Sources;

/// <summary>
/// Represents an application workflow capable of producing a ticket draft.
///
/// Ticketing consumes this abstraction without knowing which module generated
/// the underlying operational data.
/// </summary>
public interface ITicketDraftSource
{
    string Command { get; }

    string Description { get; }

    Task<TicketDraft> CreateDraftAsync(
        CancellationToken cancellationToken = default
    );
}