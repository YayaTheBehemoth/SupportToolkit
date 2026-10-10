using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.Ticketing.Models;
using SupportToolkit.Modules.Ticketing.Sources;

namespace SupportToolkit.Modules.BackupAggregator.Ticketing;

/// <summary>
/// Adapts the BackupAggregator workflow into a Ticketing draft source.
///
/// The source owns all knowledge required to obtain a BackupAggregator report.
/// Ticketing only receives the resulting provider-independent TicketDraft.
/// </summary>
public sealed class BackupReviewTicketDraftSource
    : ITicketDraftSource
{
    public string Command =>
        "backup-review";

    public string Description =>
        "Build a consolidated ticket draft from the backup inventory review.";

    public async Task<TicketDraft> CreateDraftAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * This source belongs to BackupAggregator, so its input mode is
         * controlled by BackupAggregator's runtime configuration rather than
         * Ticketing's.
         *
         * This allows fixture BackupAggregator data to be passed to a real
         * external ticketing provider later without coupling the two modes.
         */
        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment(
                BackupAggregatorModule.ModuleCommand
            );

        AggregatedBackupReport report;

        if (runtimeOptions.Mode
            == SupportToolkitMode.Fixture)
        {
            report =
                new BackupAggregatorFixtureReviewService()
                    .BuildReport();
        }
        else
        {
            var logger =
                OperationalLogger.FromEnvironment();

            using var session =
                BackupAggregatorProductionSession.Create(
                    logger
                );

            report =
                await session.ReviewService
                    .ReviewAllTenantsAsync(
                        cancellationToken
                    );
        }

        return new BackupReviewTicketDraftFactory()
            .Create(
                report
            );
    }
}