using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Ticketing;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Application workflow for BackupAggregator.
///
/// User interfaces decide what operation the user wants. This workflow
/// executes it against the sources the host chose (synthetic data or the
/// real systems) and sends tickets to the host's ticket sink.
///
/// Both raw CLI commands and interactive user interfaces should call this
/// workflow rather than duplicating BackupAggregator orchestration.
/// </summary>
public sealed class BackupAggregatorWorkflow
{
    private readonly IBackupAggregatorSources _sources;

    private readonly IBackupTicketSink _tickets;

    public BackupAggregatorWorkflow(
        IBackupAggregatorSources sources,
        IBackupTicketSink tickets)
    {
        _sources =
            sources
            ?? throw new ArgumentNullException(nameof(sources));

        _tickets =
            tickets
            ?? throw new ArgumentNullException(nameof(tickets));
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken = default)
    {
        return _sources.GetModeAsync(
            cancellationToken
        );
    }

    public Task<AggregatedBackupReport>
        ReviewAllTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        return _sources.ReviewAllTenantsAsync(
            cancellationToken
        );
    }

    public Task<AggregatedBackupReport>
        ReviewTenantAsync(
            string tenantName,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                tenantName))
        {
            throw new ArgumentException(
                "Tenant name cannot be empty.",
                nameof(tenantName)
            );
        }

        return _sources.ReviewTenantAsync(
            tenantName.Trim(),
            cancellationToken
        );
    }

    /// <summary>
    /// Builds a ticket draft from a completed scoped review.
    ///
    /// A ticket asserts that every monitored tenant was reviewed, so a
    /// scoped review that is incomplete (an unmatched tenant, an
    /// unresolved scope, a failed tenant review) produces no draft at
    /// all, and the reasons are stated.
    /// </summary>
    public TicketDraft CreateScopedTicketDraft(
        ScopedBackupInventoryReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(
            result
        );

        var preparation =
            PrepareScopedTicketDraft(
                result
            );

        if (!preparation.IsReady)
        {
            throw new InvalidOperationException(
                "No ticket can be created because the monitored review " +
                "is not complete: " +
                string.Join(" ", preparation.NotReady.Reasons)
            );
        }

        return preparation.Value;
    }

    /// <summary>
    /// The one place that decides whether a scoped review may produce a
    /// ticket. A review that is missing or incomplete is a typed result,
    /// not an exception, so any interface can present the reasons.
    /// It needs no Zendesk configuration: previewing a draft does not
    /// depend on where it would be sent.
    /// </summary>
    public TicketPreparation<TicketDraft> PrepareScopedTicketDraft(
        ScopedBackupInventoryReviewResult? review)
    {
        if (review is null)
        {
            return TicketPreparation<TicketDraft>.Blocked(
                TicketNotReadyKind.NoReview,
                "Run the monitored review first."
            );
        }

        var blockers =
            review.TicketBlockers;

        if (blockers.Count > 0)
        {
            return TicketPreparation<TicketDraft>.Blocked(
                new TicketNotReady(
                    TicketNotReadyKind.ReviewIncomplete,
                    blockers
                )
            );
        }

        return TicketPreparation<TicketDraft>.Ready(
            new BackupReviewTicketDraftFactory()
                .Create(
                    review.Inventory,
                    review.TicketScopeNote,
                    review.TicketScopeTenantLines,
                    review.TicketStrayLines,
                    review.TicketScopeWarning
                )
        );
    }

    /// <summary>
    /// Prepares a submission for an operator to confirm: the draft, the
    /// Zendesk destination it would go to, and the review's headline
    /// numbers. Everything is validated locally, before any prompt and
    /// without calling Zendesk or exposing credentials.
    /// </summary>
    public async Task<TicketPreparation<TicketSubmissionPlan>>
        PrepareScopedTicketSubmissionAsync(
            ScopedBackupInventoryReviewResult? review,
            CancellationToken cancellationToken = default)
    {
        var draft =
            PrepareScopedTicketDraft(
                review
            );

        if (!draft.IsReady)
        {
            return TicketPreparation<TicketSubmissionPlan>.Blocked(
                draft.NotReady
            );
        }

        TicketDestinationPreview destination;

        try
        {
            destination =
                await GetTicketSubmissionTargetAsync(
                    cancellationToken
                );
        }
        catch (InvalidOperationException exception)
        {
            return TicketPreparation<TicketSubmissionPlan>.Blocked(
                TicketNotReadyKind.DestinationUnavailable,
                exception.Message
            );
        }

        return TicketPreparation<TicketSubmissionPlan>.Ready(
            new TicketSubmissionPlan(
                draft.Value,
                destination,
                ReviewSummary.From(review!)
            )
        );
    }

    /// <summary>
    /// Submits exactly the plan an operator confirmed. It fails closed if
    /// the configured destination is no longer the confirmed one. The
    /// idempotency key and the Zendesk write path are the existing ones.
    /// </summary>
    public async Task<CreatedTicket>
        SubmitPlannedTicketAsync(
            TicketSubmissionPlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            plan
        );

        return await SubmitTicketDraftAsync(
            plan.Draft,
            cancellationToken,
            plan.Destination
        );
    }

    /// <summary>
    /// Resolve and validate the currently selected Zendesk destination
    /// without calling the external API or exposing credentials to the UI.
    /// </summary>
    public Task<TicketDestinationPreview>
        GetTicketSubmissionTargetAsync(
            CancellationToken cancellationToken = default) =>
        _tickets.GetDestinationAsync(
            cancellationToken);

    private Task<CreatedTicket>
        SubmitTicketDraftAsync(
            TicketDraft draft,
            CancellationToken cancellationToken,
            TicketDestinationPreview? confirmedDestination = null) =>
        _tickets.SubmitAsync(
            draft,
            confirmedDestination,
            cancellationToken);
}
