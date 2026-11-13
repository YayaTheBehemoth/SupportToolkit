using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Ticketing.Models;

namespace SupportToolkit.Modules.BackupAggregator.Ticketing;

/// <summary>
/// Where a review's ticket goes. Ticketing is always real, whatever the
/// review's mode: a fixture review submits only through an explicit
/// submit with a configured destination.
/// </summary>
public interface IBackupTicketSink
{
    /// <summary>
    /// The configured destination, resolved and validated locally without
    /// calling the ticketing system or exposing credentials.
    /// </summary>
    Task<TicketDestinationPreview> GetDestinationAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Submits the draft. With a confirmed destination, fails closed if
    /// the configured destination has changed since it was confirmed.
    /// </summary>
    Task<CreatedTicket> SubmitAsync(
        TicketDraft draft,
        TicketDestinationPreview? confirmedDestination,
        CancellationToken cancellationToken);
}
