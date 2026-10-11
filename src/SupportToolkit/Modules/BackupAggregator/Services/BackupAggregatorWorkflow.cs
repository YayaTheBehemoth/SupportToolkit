using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Ticketing.Models;
using SupportToolkit.Core.Ticketing.Services;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Ticketing;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Zendesk.Transport;

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

    public BackupAggregatorWorkflow(
        SupportToolkitConfigurationResolver configurationResolver)
    {
        _configurationResolver =
            configurationResolver
            ?? throw new ArgumentNullException(
                nameof(configurationResolver)
            );
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
    /// Builds a ticket draft from an already completed full backup review.
    ///
    /// This overload is used by interactive workflows so preview and
    /// submission operate on the exact review the operator has inspected.
    /// </summary>
    public TicketDraft CreateTicketDraft(
        AggregatedBackupReport report)
    {
        ArgumentNullException.ThrowIfNull(
            report
        );

        return new BackupReviewTicketDraftFactory()
            .Create(
                report
            );
    }

    /// <summary>
    /// Performs a fresh full review and creates a ticket draft.
    ///
    /// This keeps raw CLI usage stateless while interactive callers may use
    /// CreateTicketDraft(report) to work from an existing reviewed snapshot.
    /// </summary>
    public async Task<TicketDraft>
        CreateTicketDraftAsync(
            CancellationToken cancellationToken = default)
    {
        var report =
            await ReviewAllTenantsAsync(
                cancellationToken
            );

        return CreateTicketDraft(
            report
        );
    }

    /// <summary>
    /// Submits a ticket representing an already completed full review.
    ///
    /// Write permission is checked independently from the BackupAggregator
    /// runtime mode. Fixture reviews may therefore be submitted to a configured
    /// Zendesk connection when the active profile explicitly permits writes.
    /// </summary>
    public async Task<CreatedTicket>
        SubmitTicketAsync(
            AggregatedBackupReport report,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            report
        );

        var allowWrites =
            await _configurationResolver
                .GetAllowWritesAsync(
                    cancellationToken
                );

        if (!allowWrites)
        {
            throw new InvalidOperationException(
                "External writes are disabled for the active profile. " +
                "Enable writes for that profile or set " +
                "SUPPORTTOOLKIT_ALLOW_WRITES=true explicitly."
            );
        }

        var draft =
            CreateTicketDraft(
                report
            );

        return await SubmitTicketDraftAsync(
            draft,
            cancellationToken
        );
    }

    /// <summary>
    /// Performs a fresh full review and submits the resulting ticket.
    ///
    /// This overload exists for stateless raw CLI usage.
    /// Interactive callers should normally submit an already reviewed report.
    /// </summary>
    public async Task<CreatedTicket>
        SubmitTicketAsync(
            CancellationToken cancellationToken = default)
    {
        var report =
            await ReviewAllTenantsAsync(
                cancellationToken
            );

        return await SubmitTicketAsync(
            report,
            cancellationToken
        );
    }

    private async Task<CreatedTicket>
        SubmitTicketDraftAsync(
            TicketDraft draft,
            CancellationToken cancellationToken)
    {
        var resolvedConnection =
            await _configurationResolver
                .GetZendeskConnectionAsync(
                    cancellationToken
                );

        var zendeskOptions =
            ZendeskOptions.Create(
                resolvedConnection.Subdomain,
                resolvedConnection.ClientId,
                resolvedConnection.ClientSecret
            );

        var logger =
            OperationalLogger.FromEnvironment();

        using var session =
            TicketingProductionSession.Create(
                zendeskOptions,
                logger
            );

        var idempotencyKey =
            TicketIdempotencyKeyFactory.Create(
                "backup-review",
                draft
            );

        return await session.TicketProvider
            .CreateTicketAsync(
                draft,
                idempotencyKey,
                cancellationToken
            );
    }

    private async Task<BackupAggregatorProductionSession>
        CreateProductionSessionAsync(
            CancellationToken cancellationToken)
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

        return BackupAggregatorProductionSession
            .Create(
                acronisOptions,
                logger
            );
    }

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