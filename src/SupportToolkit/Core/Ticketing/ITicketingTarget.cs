using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Core.Ticketing;

/// <summary>
/// A resolved, locally validated ticket destination in one ticketing
/// system. Each ticketing provider builds its own from configuration;
/// workflows use only this contract, so adding a system does not change
/// them.
///
/// Resolving a target calls nothing. Only OpenSession prepares a
/// connection, and only a ticket provider call writes.
/// </summary>
public interface ITicketingTarget
{
    /// <summary>What the operator confirms before a write.</summary>
    TicketDestinationPreview Destination { get; }

    /// <summary>
    /// Non-secret settings for status displays, in display order.
    /// </summary>
    IReadOnlyList<StatusDetail> Details { get; }

    /// <summary>
    /// The real instance and routing, for the retry identity. Independent
    /// of profile and destination aliases, so renaming one does not
    /// change a ticket's identity.
    /// </summary>
    string IdempotencyIdentity { get; }

    ITicketingSession OpenSession(OperationalLogger logger);
}

/// <summary>
/// An open ticketing connection. Disposing it releases the transport.
/// </summary>
public interface ITicketingSession : IDisposable
{
    ITicketProvider TicketProvider { get; }
}
