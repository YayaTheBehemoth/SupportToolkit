
using System.Diagnostics;
using System.Net;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Services;

public sealed class BackupInventoryReviewService
{
    private const int DefaultMaxConcurrentTenants = 3;

    private readonly AcronisTenantResolver _tenantResolver;
    private readonly TenantInventoryReviewService _tenantReviewer;
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
                "Tenant concurrency must be at least 1.");
        }

        _tenantResolver = tenantResolver;
        _tenantReviewer = new TenantInventoryReviewService(
            microsoft365Inventory,
            deviceInventory,
            normalizer);
        _logger = logger;
        _maxConcurrentTenants = maxConcurrentTenants;
    }

    public async Task<AggregatedBackupReport> ReviewTenantAsync(
        string tenantName,
        CancellationToken cancellationToken = default)
    {
        _logger?.Info(
            "Starting backup inventory review.");

        var tenant =
            await _tenantResolver.ResolveExactAsync(
                tenantName,
                cancellationToken);

        _logger?.Info(
            "Tenant resolved.");

        var result =
            await _tenantReviewer.ReviewResolvedTenantAsync(
                tenant,
                cancellationToken);

        _logger?.Info(
            $"Microsoft 365 resources: " +
            $"{result.Microsoft365ResourceCount}.");

        _logger?.Info(
            $"Device resources: " +
            $"{result.DeviceResourceCount}.");

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
            $"{report.UnknownRowsCount} unclassified.");

        return report;
    }

    public async Task<AggregatedBackupReport> ReviewAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch =
            Stopwatch.StartNew();

        _logger?.Info(
            "Starting all-tenant backup inventory review.");

        var tenants =
            await _tenantResolver.GetCustomerTenantsAsync(
                cancellationToken);

        _logger?.Info(
            $"Customer tenants selected: {tenants.Count}.");

        /*
         * An empty customer population is not a successful backup review.
         *
         * It may indicate lost API visibility, unexpected tenant kinds,
         * incorrect scoping, or a genuinely empty hierarchy.
         *
         * In every case, SupportToolkit cannot claim the customer
         * estate was reviewed when no customers were discovered.
         */
        if (tenants.Count == 0)
        {
            throw new InvalidOperationException(
                "Acronis tenant discovery returned zero customer tenants. " +
                "The all-tenant backup review cannot be considered complete. " +
                "Verify the configured Acronis connection and tenant scope.");
        }

        _logger?.Info(
            $"Tenant review concurrency limit: " +
            $"{_maxConcurrentTenants}.");

        /*
         * Independent tenants are reviewed concurrently.
         *
         * Outcomes are stored by their original index so that
         * reporting order stays deterministic.
         */
        var outcomes =
            new TenantReviewOutcome?[tenants.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, tenants.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism =
                    _maxConcurrentTenants,

                CancellationToken =
                    cancellationToken
            },
            async (index, iterationCancellationToken) =>
            {
                var tenant = tenants[index];

                _logger?.Info(
                    $"Starting tenant review " +
                    $"{index + 1}/{tenants.Count}.");

                outcomes[index] =
                    await ReviewTenantSafelyAsync(
                        tenant,
                        index + 1,
                        tenants.Count,
                        iterationCancellationToken);

                _logger?.Info(
                    $"Completed tenant review " +
                    $"{index + 1}/{tenants.Count}.");
            });

        var reports =
            new List<TenantBackupReport>();

        var failures =
            new List<TenantBackupReviewFailure>();

        foreach (var outcome in outcomes)
        {
            if (outcome is null)
            {
                throw new InvalidOperationException(
                    "A tenant review completed without producing an outcome.");
            }

            if (outcome.Report is not null)
            {
                reports.Add(outcome.Report);
            }

            if (outcome.Failure is not null)
            {
                failures.Add(outcome.Failure);
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
            $"{stopwatch.Elapsed.TotalSeconds:F1}s elapsed.");

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
                await _tenantReviewer.ReviewResolvedTenantAsync(
                    tenant,
                    cancellationToken);

            return new TenantReviewOutcome(
                result.Report,
                null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TenantReviewStageException exception)
        {
            var underlyingException =
                exception.InnerException ?? exception;

            /*
             * Never log raw exception messages here.
             *
             * HTTP and vendor exception messages can contain
             * URLs, cursor tokens, request identifiers, or other
             * values not handled by UUID redaction.
             *
             * Preserve the stage, exception type, and HTTP status.
             */
            _logger?.Debug(
                $"Tenant review {tenantNumber}/{tenantCount} " +
                $"failed during {exception.Stage}: " +
                $"{DescribeFailure(underlyingException)}.");

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
                            underlyingException)
                });
        }
        catch (Exception exception)
        {
            _logger?.Debug(
                $"Tenant review {tenantNumber}/{tenantCount} " +
                $"failed unexpectedly: " +
                $"{DescribeFailure(exception)}.");

            return new TenantReviewOutcome(
                null,
                new TenantBackupReviewFailure
                {
                    TenantName =
                        tenant.Name,

                    Stage =
                        "Unexpected error",

                    Reason =
                        GetSafeFailureReason(exception)
                });
        }
    }

    /// <summary>
    /// Structured diagnostic information derived only from
    /// exception type and HTTP status.
    ///
    /// Does not include Exception.Message, request URLs,
    /// response bodies, or exception ToString() output.
    /// </summary>
    private static string DescribeFailure(
        Exception exception)
    {
        var typeName =
            exception.GetType().Name;

        if (exception is HttpRequestException httpException
            && httpException.StatusCode is HttpStatusCode statusCode)
        {
            return
                $"{typeName} (HTTP {(int)statusCode})";
        }

        return typeName;
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

    private sealed record TenantReviewOutcome(
        TenantBackupReport? Report,
        TenantBackupReviewFailure? Failure);
}
