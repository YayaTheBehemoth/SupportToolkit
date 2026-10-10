using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Modules.Ticketing.Models;
using SupportToolkit.Modules.Ticketing.Sources;
using SupportToolkit.Providers.Acronis.Transport;

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
    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public string Command =>
        "backup-review";

    public string Description =>
        "Build a consolidated ticket draft from the backup inventory review.";

    public BackupReviewTicketDraftSource(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver)
            );
    }

    public async Task<TicketDraft> CreateDraftAsync(
        CancellationToken cancellationToken = default)
    {
        /*
         * This source belongs to BackupAggregator, so its input mode is
         * controlled by BackupAggregator's runtime configuration rather than
         * Ticketing's.
         *
         * This allows fixture BackupAggregator data to be passed to a real
         * external ticketing provider without coupling the two modes.
         */
        var mode =
            await _configurationResolver
                .GetModuleModeAsync(
                    BackupAggregatorModule.ModuleCommand,
                    cancellationToken
                );

        AggregatedBackupReport report;

        if (mode
            == SupportToolkitMode.Fixture)
        {
            report =
                new BackupAggregatorFixtureReviewService()
                    .BuildReport();
        }
        else
        {
            var resolvedConnection =
                await _configurationResolver
                    .GetAcronisConnectionAsync(
                        cancellationToken
                    );

            var acronisOptions =
                AcronisOptions.Create(
                    resolvedConnection.DatacenterUrl,
                    resolvedConnection.ClientId,
                    resolvedConnection.ClientSecret
                );

            var logger =
                OperationalLogger.FromEnvironment();

            using var session =
                BackupAggregatorProductionSession.Create(
                    acronisOptions,
                    logger
                );

            /*
             * The ticket source intentionally represents the consolidated
             * backup review, so production draft generation reviews all
             * tenants rather than accepting a tenant selector.
             */
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