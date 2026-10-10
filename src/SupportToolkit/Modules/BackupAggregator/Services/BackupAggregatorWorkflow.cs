using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Application workflow for BackupAggregator inventory reviews.
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
            return new BackupAggregatorFixtureReviewService()
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
            new BackupAggregatorFixtureReviewService()
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