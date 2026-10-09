using System.Diagnostics;
using System.Net;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupInventoryReviewService
{
    private const int DefaultMaxConcurrentTenants = 3;

    private readonly AcronisTenantResolver _tenantResolver;

    private readonly IAcronisMicrosoft365InventoryProvider
        _microsoft365Inventory;

    private readonly IAcronisDeviceInventoryProvider
        _deviceInventory;

    private readonly BackupInventoryNormalizer _normalizer;

    private readonly OperationalLogger? _logger;

    private readonly int _maxConcurrentTenants;

    public BackupInventoryReviewService(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer,
        OperationalLogger? logger = null,
        int maxConcurrentTenants = DefaultMaxConcurrentTenants)
    {
        if (maxConcurrentTenants < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentTenants),
                "Tenant concurrency must be at least 1."
            );
        }

        _tenantResolver = tenantResolver;
        _microsoft365Inventory = microsoft365Inventory;
        _deviceInventory = deviceInventory;
        _normalizer = normalizer;
        _logger = logger;
        _maxConcurrentTenants = maxConcurrentTenants;
    }

    public async Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Starting backup inventory review."
        );

        var tenant =
            await _tenantResolver.ResolveExactAsync(
                tenantName,
                cancellationToken
            );

        _logger?.Info(
            "Tenant resolved."
        );

        var result =
            await ReviewResolvedTenantAsync(
                tenant,
                cancellationToken
            );

        _logger?.Info(
            $"Microsoft 365 resources: " +
            $"{result.Microsoft365ResourceCount}."
        );

        _logger?.Info(
            $"Device resources: " +
            $"{result.DeviceResourceCount}."
        );

        var report =
            new AggregatedBackupReport
            {
                Tenants =
                    [
                        result.Report
                    ]
            };

        _logger?.Info(
            $"Backup inventory review complete: " +
            $"{report.RowsChecked} checked, " +
            $"{report.FindingsCount} require attention, " +
            $"{report.UnknownRowsCount} unclassified."
        );

        return report;
    }

    public async Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch =
            Stopwatch.StartNew();

        _logger?.Info(
            "Starting all-tenant backup inventory review."
        );

        var tenants =
            await _tenantResolver.GetCustomerTenantsAsync(
                cancellationToken
            );

        _logger?.Info(
            $"Customer tenants selected: {tenants.Count}."
        );

        _logger?.Info(
            $"Tenant review concurrency limit: " +
            $"{_maxConcurrentTenants}."
        );

        /*
         * Each tenant remains internally sequential.
         *
         * Only independent tenant reviews are allowed to overlap. This keeps
         * pagination and provider behavior unchanged while removing the
         * all-tenant scan's strictly sequential scaling ceiling.
         *
         * Outcomes are stored by original tenant index so final reporting
         * remains deterministic even though tenant completion order is not.
         */
        var outcomes =
            new TenantReviewOutcome?[tenants.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(
                0,
                tenants.Count
            ),
            new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    _maxConcurrentTenants,

                CancellationToken =
                    cancellationToken
            },
            async (
                index,
                iterationCancellationToken) =>
            {
                var tenant =
                    tenants[index];

                _logger?.Info(
                    $"Starting tenant review " +
                    $"{index + 1}/{tenants.Count}."
                );

                outcomes[index] =
                    await ReviewTenantSafelyAsync(
                        tenant,
                        index + 1,
                        tenants.Count,
                        iterationCancellationToken
                    );

                _logger?.Info(
                    $"Completed tenant review " +
                    $"{index + 1}/{tenants.Count}."
                );
            }
        );

        var reports =
            new List<TenantBackupReport>();

        var failures =
            new List<TenantBackupReviewFailure>();

        foreach (var outcome in outcomes)
        {
            if (outcome is null)
            {
                throw new InvalidOperationException(
                    "A tenant review completed without producing an outcome."
                );
            }

            if (outcome.Report is not null)
            {
                reports.Add(
                    outcome.Report
                );
            }

            if (outcome.Failure is not null)
            {
                failures.Add(
                    outcome.Failure
                );
            }
        }

        var report =
            new AggregatedBackupReport
            {
                Tenants =
                    reports.AsReadOnly(),

                Failures =
                    failures.AsReadOnly()
            };

        stopwatch.Stop();

        _logger?.Info(
            $"All-tenant backup review complete: " +
            $"{report.TenantsAttempted} attempted, " +
            $"{report.TenantCount} reviewed, " +
            $"{report.FailedTenantCount} failed, " +
            $"{report.RowsChecked} resources checked, " +
            $"{stopwatch.Elapsed.TotalSeconds:F1}s elapsed."
        );

        return report;
    }

    private async Task<TenantReviewOutcome>
        ReviewTenantSafelyAsync(
            TenantDto tenant,
            int tenantNumber,
            int tenantCount,
            CancellationToken cancellationToken)
    {
        try
        {
            var result =
                await ReviewResolvedTenantAsync(
                    tenant,
                    cancellationToken
                );

            return new TenantReviewOutcome(
                result.Report,
                null
            );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TenantReviewStageException exception)
        {
            _logger?.Debug(
                $"Tenant review {tenantNumber}/{tenantCount} " +
                $"failed during {exception.Stage}: " +
                $"{exception.InnerException?.GetType().Name ?? exception.GetType().Name}: " +
                $"{exception.InnerException?.Message ?? exception.Message}"
            );

            return new TenantReviewOutcome(
                null,
                new TenantBackupReviewFailure
                {
                    TenantName =
                        tenant.Name,

                    Stage =
                        exception.Stage,

                    Reason =
                        GetSafeFailureReason(
                            exception.InnerException
                            ?? exception
                        )
                }
            );
        }
        catch (Exception exception)
        {
            _logger?.Debug(
                $"Tenant review {tenantNumber}/{tenantCount} " +
                $"failed unexpectedly: " +
                $"{exception.GetType().Name}: " +
                $"{exception.Message}"
            );

            return new TenantReviewOutcome(
                null,
                new TenantBackupReviewFailure
                {
                    TenantName =
                        tenant.Name,

                    Stage =
                        "Unexpected error",

                    Reason =
                        GetSafeFailureReason(
                            exception
                        )
                }
            );
        }
    }

    private async Task<TenantReviewResult>
        ReviewResolvedTenantAsync(
            TenantDto tenant,
            CancellationToken cancellationToken)
    {
        IReadOnlyList<AcronisMicrosoft365ResourceDto>
            microsoft365Resources;

        try
        {
            microsoft365Resources =
                await _microsoft365Inventory
                    .GetResourcesForTenantAsync(
                        tenant.Id,
                        cancellationToken
                    );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TenantReviewStageException(
                "Microsoft 365 inventory",
                exception
            );
        }

        IReadOnlyList<AcronisDeviceResourceDto>
            deviceResources;

        try
        {
            deviceResources =
                await _deviceInventory
                    .GetResourcesForTenantAsync(
                        tenant.Id,
                        cancellationToken
                    );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new TenantReviewStageException(
                "Device inventory",
                exception
            );
        }

        var tenantReport =
            _normalizer.BuildTenantReport(
                tenant.Name,
                microsoft365Resources,
                deviceResources
            );

        return new TenantReviewResult(
            tenantReport,
            microsoft365Resources.Count,
            deviceResources.Count
        );
    }

    private static string GetSafeFailureReason(
        Exception exception)
    {
        if (exception is HttpRequestException httpException
            && httpException.StatusCode is HttpStatusCode statusCode)
        {
            return
                $"Acronis returned HTTP " +
                $"{(int)statusCode} ({statusCode}).";
        }

        if (exception is TaskCanceledException)
        {
            return
                "The Acronis request timed out.";
        }

        return
            "The tenant review failed. " +
            "Enable debug logging for technical details.";
    }

    private sealed record TenantReviewResult(
        TenantBackupReport Report,
        int Microsoft365ResourceCount,
        int DeviceResourceCount
    );

    private sealed record TenantReviewOutcome(
        TenantBackupReport? Report,
        TenantBackupReviewFailure? Failure
    );

    private sealed class TenantReviewStageException
        : Exception
    {
        public string Stage { get; }

        public TenantReviewStageException(
            string stage,
            Exception innerException)
            : base(
                $"{stage} failed.",
                innerException
            )
        {
            Stage = stage;
        }
    }
}