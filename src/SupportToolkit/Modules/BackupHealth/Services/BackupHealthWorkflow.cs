using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Modules.BackupHealth.Services;

/// <summary>
/// Application workflow for BackupHealth.
///
/// User interfaces decide when BackupHealth should run.
/// This workflow decides how the evaluation is executed according to the
/// active SupportToolkit runtime configuration.
///
/// Both raw CLI commands and interactive user interfaces should call this
/// workflow rather than duplicating BackupHealth orchestration.
/// </summary>
public sealed class BackupHealthWorkflow
{
    private static readonly TimeSpan StaleAfter =
        TimeSpan.FromHours(
            48
        );

    private static readonly DateTimeOffset FixtureNow =
        new(
            2026,
            10,
            1,
            12,
            0,
            0,
            TimeSpan.Zero
        );

    private readonly SupportToolkitConfigurationResolver
        _configurationResolver;

    public BackupHealthWorkflow(
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
                BackupHealthModule.ModuleCommand,
                cancellationToken
            );
    }

    public async Task<BackupHealthResult>
        EvaluateAsync(
            CancellationToken cancellationToken = default)
    {
        var mode =
            await GetModeAsync(
                cancellationToken
            );

        var logger =
            OperationalLogger.FromEnvironment();

        logger.Info(
            $"Starting {BackupHealthModule.ModuleCommand} in " +
            $"{mode.ToString().ToLowerInvariant()} mode."
        );

        return mode switch
        {
            SupportToolkitMode.Fixture =>
                await EvaluateFixtureAsync(
                    logger,
                    cancellationToken
                ),

            SupportToolkitMode.Production =>
                await EvaluateProductionAsync(
                    logger,
                    cancellationToken
                ),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported SupportToolkit mode: {mode}."
                )
        };
    }

    private async Task<BackupHealthResult>
        EvaluateProductionAsync(
            OperationalLogger logger,
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

        using var session =
            BackupHealthProductionSession.Create(
                acronisOptions,
                logger
            );

        return await EvaluateAsync(
            session.HealthService,
            TimeProvider.System,
            logger,
            cancellationToken
        );
    }

    private static Task<BackupHealthResult>
        EvaluateFixtureAsync(
            OperationalLogger logger,
            CancellationToken cancellationToken)
    {
        var fixtureDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Acronis"
            );

        var provider =
            new FixtureAcronisProvider(
                fixtureDirectory
            );

        var healthService =
            new BackupHealthService(
                provider
            );

        var timeProvider =
            new FixedTimeProvider(
                FixtureNow
            );

        return EvaluateAsync(
            healthService,
            timeProvider,
            logger,
            cancellationToken
        );
    }

    private static async Task<BackupHealthResult>
        EvaluateAsync(
            BackupHealthService healthService,
            TimeProvider timeProvider,
            OperationalLogger logger,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            healthService
        );

        var exceptionEngine =
            new BackupExceptionEngine(
                timeProvider,
                StaleAfter
            );

        logger.Info(
            "Loading backup health snapshot."
        );

        var snapshot =
            await healthService
                .GetSnapshotAsync(
                    cancellationToken
                );

        logger.Info(
            $"Snapshot loaded: " +
            $"{snapshot.Resources.Count} resource(s), " +
            $"{snapshot.Diagnostics.Count} diagnostic(s)."
        );

        var exceptions =
            exceptionEngine.Evaluate(
                snapshot.Resources
            );

        logger.Info(
            $"Exception evaluation completed: " +
            $"{exceptions.Count} exception(s)."
        );

        return new BackupHealthResult
        {
            Snapshot =
                snapshot,

            Exceptions =
                exceptions
        };
    }

    private sealed class FixedTimeProvider
        : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(
            DateTimeOffset utcNow)
        {
            _utcNow =
                utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}