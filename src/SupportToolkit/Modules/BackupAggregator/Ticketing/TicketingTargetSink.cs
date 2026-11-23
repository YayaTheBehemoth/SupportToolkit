using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Services;

namespace SupportToolkit.Modules.BackupAggregator.Ticketing;

/// <summary>
/// Sends a review's ticket to whichever ticketing system the profile
/// selects. The target is resolved again for every call, so a confirmed
/// destination is checked against the configuration as it is at the
/// moment of the write.
/// </summary>
public sealed class TicketingTargetSink : IBackupTicketSink
{
    private const string SourceCommand = "backup-review";

    private readonly Func<CancellationToken, Task<ITicketingTarget>>
        _resolveTarget;

    private readonly Func<OperationalLogger> _logger;

    public TicketingTargetSink(
        Func<CancellationToken, Task<ITicketingTarget>> resolveTarget,
        Func<OperationalLogger>? logger = null)
    {
        _resolveTarget =
            resolveTarget
            ?? throw new ArgumentNullException(nameof(resolveTarget));

        _logger = logger ?? (() => OperationalLogger.FromEnvironment());
    }

    public async Task<TicketDestinationPreview> GetDestinationAsync(
        CancellationToken cancellationToken) =>
        (await _resolveTarget(cancellationToken)).Destination;

    public async Task<CreatedTicket> SubmitAsync(
        TicketDraft draft,
        TicketDestinationPreview? confirmedDestination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        /*
         * Resolve before opening a session: missing or invalid routing
         * fails locally, before any request.
         */
        var target = await _resolveTarget(cancellationToken);

        if (confirmedDestination is not null
            && !target.Destination.IsSameAs(confirmedDestination))
        {
            throw new InvalidOperationException(
                $"The {target.Destination.SystemName} destination changed " +
                "since confirmation. No ticket was submitted. Review the " +
                "destination and try again.");
        }

        var idempotencyKey =
            TicketIdempotencyKeyFactory.Create(
                SourceCommand,
                draft,
                target.IdempotencyIdentity);

        using var session = target.OpenSession(_logger());

        return await session.TicketProvider.CreateTicketAsync(
            draft,
            idempotencyKey,
            cancellationToken);
    }
}
