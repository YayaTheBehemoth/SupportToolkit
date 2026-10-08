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
    private readonly AcronisTenantResolver _tenantResolver;

    private readonly IAcronisMicrosoft365InventoryProvider
        _microsoft365Inventory;

    private readonly IAcronisDeviceInventoryProvider
        _deviceInventory;

    private readonly BackupInventoryNormalizer _normalizer;

    private readonly OperationalLogger? _logger;

    public BackupInventoryReviewService(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer,
        OperationalLogger? logger = null)
    {
        _tenantResolver = tenantResolver;
        _microsoft365Inventory = microsoft365Inventory;
        _deviceInventory = deviceInventory;
        _normalizer = normalizer;
        _logger = logger;
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

        var reports =
            new List<TenantBackupReport>();

        var failures =
            new List<TenantBackupReviewFailure>();

        /*
         * Deliberately sequential for the first production validation.
         *
         * Once we know how heterogeneous tenants behave against the two
         * inventory providers, this can be changed to bounded concurrency
         * without changing the reporting contract.
         */
        for (var index = 0; index < tenants.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tenant =
                tenants[index];

            _logger?.Info(
                $"Reviewing tenant {index + 1}/{tenants.Count}."
            );

            try
            {
                var result =
                    await ReviewResolvedTenantAsync(
                        tenant,
                        cancellationToken
                    );

                reports.Add(
                    result.Report
                );
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TenantReviewStageException exception)
            {
                failures.Add(
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

                _logger?.Debug(
                    $"Tenant review failed during " +
                    $"{exception.Stage}: " +
                    $"{exception.InnerException?.GetType().Name ?? exception.GetType().Name}: " +
                    $"{exception.InnerException?.Message ?? exception.Message}"
                );
            }
            catch (Exception exception)
            {
                failures.Add(
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

                _logger?.Debug(
                    $"Tenant review failed unexpectedly: " +
                    $"{exception.GetType().Name}: " +
                    $"{exception.Message}"
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

        _logger?.Info(
            $"All-tenant backup review complete: " +
            $"{report.TenantsAttempted} attempted, " +
            $"{report.TenantCount} reviewed, " +
            $"{report.FailedTenantCount} failed, " +
            $"{report.RowsChecked} resources checked."
        );

        return report;
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