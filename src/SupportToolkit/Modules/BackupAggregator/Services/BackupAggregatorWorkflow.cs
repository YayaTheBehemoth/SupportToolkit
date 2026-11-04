
using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Modules.BackupAggregator.Production;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Application workflow for BackupAggregator.
///
/// User interfaces decide what operation the user wants.
/// This workflow decides how that operation is executed according to the
/// active SupportToolkit runtime configuration.
///
/// Both raw CLI commands and interactive user interfaces should call this
/// workflow rather than duplicating BackupAggregator orchestration.
/// </summary>
public sealed class BackupAggregatorWorkflow
{
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    private readonly Action<string>? _progress;

    private readonly ReviewRunMode _runMode;

    /// <param name="progress">
    /// Where operator-facing progress messages from a production review
    /// go (for example while waiting for in-progress backups). Supplied
    /// by the host; the workflow itself never writes to a console. Null
    /// discards them.
    /// </param>
    /// <param name="runMode">
    /// Chosen by the host. Manual (the default) never waits for
    /// in-progress backups; Automatic waits as the profile says.
    /// </param>
    public BackupAggregatorWorkflow(
        SupportToolkitConfigurationResolver configurationResolver,
        Action<string>? progress = null,
        ReviewRunMode runMode = ReviewRunMode.Manual)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver)
            );

        _progress = progress;
        _runMode = runMode;
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken = default)
    {
        return _configurationResolver
            .GetModuleModeAsync(
                BackupAggregatorModule.ModuleCommand,
                cancellationToken
            );
    }

    public async Task<AggregatedBackupReport>
        ReviewAllTenantsAsync(
            CancellationToken cancellationToken = default)
    {
        var mode =
            await GetModeAsync(
                cancellationToken
            );

        if (mode
            == SupportToolkitMode.Fixture)
        {
            return new BackupAggregatorFixtureFactory()
                .BuildReport();
        }

        using var session =
            await CreateProductionSessionAsync(
                cancellationToken
            );

        return await session.ReviewService
            .ReviewAllTenantsAsync(
                cancellationToken
            );
    }

    public async Task<AggregatedBackupReport>
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

        var normalizedTenantName =
            tenantName.Trim();

        var mode =
            await GetModeAsync(
                cancellationToken
            );

        if (mode
            == SupportToolkitMode.Fixture)
        {
            return ReviewFixtureTenant(
                normalizedTenantName
            );
        }

        using var session =
            await CreateProductionSessionAsync(
                cancellationToken
            );

        return await session.ReviewService
            .ReviewTenantAsync(
                normalizedTenantName,
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

        ZendeskTicketDestinationPreview destination;

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
    public Task<ZendeskTicketDestinationPreview>
        GetTicketSubmissionTargetAsync(
            CancellationToken cancellationToken = default) =>
        BackupAggregatorProduction.GetTicketDestinationAsync(
            _configurationResolver,
            cancellationToken);

    private Task<CreatedTicket>
        SubmitTicketDraftAsync(
            TicketDraft draft,
            CancellationToken cancellationToken,
            ZendeskTicketDestinationPreview? confirmedDestination = null) =>
        BackupAggregatorProduction.SubmitTicketDraftAsync(
            _configurationResolver,
            draft,
            confirmedDestination,
            cancellationToken);

    private Task<BackupAggregatorProductionSession>
        CreateProductionSessionAsync(
            CancellationToken cancellationToken) =>
        BackupAggregatorProduction.CreateSessionAsync(
            _configurationResolver,
            _runMode,
            _progress,
            cancellationToken);

    private static AggregatedBackupReport ReviewFixtureTenant(
        string tenantName)
    {
        var completeReport =
            new BackupAggregatorFixtureFactory()
                .BuildReport();

        var matches =
            completeReport.Tenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.TenantName,
                            tenantName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Fixture tenant '{tenantName}' was not found."
            );
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"More than one fixture tenant named " +
                $"'{tenantName}' was found."
            );
        }

        return new AggregatedBackupReport
        {
            Tenants =
            [
                matches[0]
            ]
        };
    }
}
