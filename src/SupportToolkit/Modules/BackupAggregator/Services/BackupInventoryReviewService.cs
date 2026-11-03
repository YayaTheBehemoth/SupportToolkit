
using System.Diagnostics;
using System.Net;
using SupportToolkit.Core.Logging;
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
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

    private readonly TenantInventoryReviewService _tenantReviewer;

    private readonly OperationalLogger? _logger;

    private readonly int _maxConcurrentTenants;

    private readonly InProgressWaitOptions _inProgressWait;

    private readonly TimeProvider _timeProvider;

    private readonly Action<string>? _progress;

    public BackupInventoryReviewService(
        AcronisTenantResolver tenantResolver,
        IAcronisMicrosoft365InventoryProvider microsoft365Inventory,
        IAcronisDeviceInventoryProvider deviceInventory,
        BackupInventoryNormalizer normalizer,
        OperationalLogger? logger = null,
        int maxConcurrentTenants = DefaultMaxConcurrentTenants,
        InProgressWaitOptions? inProgressWait = null,
        TimeProvider? timeProvider = null,
        Action<string>? progress = null)
    {
        if (maxConcurrentTenants < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentTenants),
                "Tenant concurrency must be at least 1."
            );
        }

        _tenantResolver = tenantResolver;
        _tenantReviewer = new TenantInventoryReviewService(
            microsoft365Inventory,
            deviceInventory,
            normalizer
        );
        _logger = logger;
        _maxConcurrentTenants = maxConcurrentTenants;
        _inProgressWait =
            inProgressWait
            ?? InProgressWaitOptions.Disabled;
        _timeProvider =
            timeProvider
            ?? TimeProvider.System;
        _progress = progress;
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
            await _tenantReviewer.ReviewResolvedTenantAsync(
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

    /// <summary>
    /// Customer tenants available for selection. Does not review
    /// anything.
    /// </summary>
    public Task<IReadOnlyList<TenantDto>> GetCustomerTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return _tenantResolver.GetCustomerTenantsAsync(
            cancellationToken
        );
    }

    /// <summary>
    /// Every tenant of any kind, for callers that need the hierarchy.
    /// Does not review anything.
    /// </summary>
    public Task<IReadOnlyList<TenantDto>> GetAllTenantsAsync(
        CancellationToken cancellationToken = default)
    {
        return _tenantResolver.GetAllTenantsAsync(
            cancellationToken
        );
    }

    /// <summary>
    /// Preserves the existing unscoped all-tenant behavior.
    ///
    /// Monitoring entitlement is not applied to this operation.
    /// </summary>
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

        /* A zero-tenant discovery is not a successful full backup review. */
        if (tenants.Count == 0)
        {
            throw new InvalidOperationException(
                "Acronis tenant discovery returned zero customer tenants. " +
                "The all-tenant backup review cannot be considered complete. " +
                "Verify the configured Acronis connection and tenant scope.");
        }

        return await ReviewTenantsAsync(
            tenants,
            cancellationToken
        );
    }

    /// <summary>
    /// Reviews only tenants explicitly selected as inventory candidates.
    ///
    /// The selection is supplied by the caller; this method does not
    /// discover or infer Business Central customer identities.
    ///
    /// Non-selected tenants and unmatched entitlement evidence remain
    /// available through the returned selection result.
    ///
    /// This entry point is not yet connected to production workflow
    /// orchestration.
    /// </summary>
    public async Task<ScopedBackupInventoryReviewResult>
        ReviewSelectedTenantsAsync(
            MonitoringTenantSelectionResult selection,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var candidates =
            selection.InventoryCandidates;

        ValidateCandidates(candidates);

        _logger?.Info(
            $"Starting scoped backup inventory review: " +
            $"{candidates.Count} candidates."
        );

        var inventory =
            await ReviewTenantsAsync(
                candidates,
                cancellationToken
            );

        return new ScopedBackupInventoryReviewResult(
            selection,
            inventory
        );
    }

    /// <summary>
    /// Shared inventory execution mechanism.
    ///
    /// This method receives a preselected tenant set and never attempts
    /// to determine commercial monitoring entitlement.
    ///
    /// Tenant reviews remain internally sequential, while independent
    /// tenants may run concurrently.
    /// </summary>
    private async Task<AggregatedBackupReport> ReviewTenantsAsync(
        IReadOnlyList<TenantDto> tenants,
        CancellationToken cancellationToken)
    {
        var stopwatch =
            Stopwatch.StartNew();

        _logger?.Info(
            $"Tenant review concurrency limit: " +
            $"{_maxConcurrentTenants}."
        );

        /*
         * Outcomes are stored by their original tenant index.
         *
         * Parallel completion order therefore never changes the order
         * of tenants in the resulting report.
         */
        var outcomes =
            new TenantReviewOutcome?[tenants.Count];

        await RunTenantReviewsAsync(
            tenants,
            Enumerable.Range(
                0,
                tenants.Count
            ).ToArray(),
            outcomes,
            cancellationToken
        );

        var waitSummary =
            await WaitForInProgressAsync(
                tenants,
                outcomes,
                cancellationToken
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
                    failures.AsReadOnly(),

                InProgressWait =
                    waitSummary
            };

        stopwatch.Stop();

        _logger?.Info(
            $"Backup inventory review complete: " +
            $"{report.TenantsAttempted} attempted, " +
            $"{report.TenantCount} reviewed, " +
            $"{report.FailedTenantCount} failed, " +
            $"{report.RowsChecked} resources checked, " +
            $"{stopwatch.Elapsed.TotalSeconds:F1}s elapsed."
        );

        return report;
    }

    /// <summary>
    /// Reviews the tenants at the given indices with bounded
    /// concurrency, storing each outcome at its original index.
    /// </summary>
    private async Task RunTenantReviewsAsync(
        IReadOnlyList<TenantDto> tenants,
        IReadOnlyList<int> indices,
        TenantReviewOutcome?[] outcomes,
        CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            indices,
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
    }

    /// <summary>
    /// Waits for resources that are still running to finish, so a
    /// report does not describe a moment in the middle of a backup.
    ///
    /// Only tenants that still have in-progress resources are
    /// reviewed again. A tenant whose re-review fails becomes a failed
    /// tenant review, exactly as if its review had failed the first
    /// time; its earlier result is not kept. At the time limit the
    /// remaining resources stay In progress and are counted as
    /// pending, never as healthy. With nothing in progress, or waiting
    /// disabled, no delay happens and no further call is made.
    /// </summary>
    private async Task<InProgressWaitSummary?> WaitForInProgressAsync(
        IReadOnlyList<TenantDto> tenants,
        TenantReviewOutcome?[] outcomes,
        CancellationToken cancellationToken)
    {
        if (!_inProgressWait.Enabled)
        {
            return null;
        }

        var pending =
            PendingIndices(outcomes);

        if (pending.Count == 0)
        {
            return null;
        }

        var started =
            _timeProvider.GetUtcNow();

        var polls = 0;

        while (pending.Count > 0)
        {
            var elapsed =
                _timeProvider.GetUtcNow() - started;

            var remaining =
                _inProgressWait.MaxWait - elapsed;

            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            _progress?.Invoke(
                $"Waiting for in-progress backups: " +
                $"{CountInProgress(outcomes, pending)} resource(s) in " +
                $"{pending.Count} tenant(s) still running " +
                $"({FormatDuration(elapsed)} of " +
                $"{FormatDuration(_inProgressWait.MaxWait)})."
            );

            await Task.Delay(
                remaining < _inProgressWait.PollInterval
                    ? remaining
                    : _inProgressWait.PollInterval,
                _timeProvider,
                cancellationToken
            );

            polls++;

            await RunTenantReviewsAsync(
                tenants,
                pending,
                outcomes,
                cancellationToken
            );

            pending =
                PendingIndices(outcomes);
        }

        var total =
            _timeProvider.GetUtcNow() - started;

        var pendingResources =
            CountInProgress(outcomes, pending);

        _progress?.Invoke(
            pending.Count == 0
                ? $"In-progress backups finished after " +
                  $"{FormatDuration(total)}."
                : $"Stopped waiting after {FormatDuration(total)}: " +
                  $"{pendingResources} resource(s) in " +
                  $"{pending.Count} tenant(s) are still in progress."
        );

        return new InProgressWaitSummary(
            _inProgressWait.MaxWait,
            total,
            polls,
            pendingResources,
            pending.Count,
            TimedOut:
                pending.Count > 0
        );
    }

    private static List<int> PendingIndices(
        TenantReviewOutcome?[] outcomes)
    {
        var pending =
            new List<int>();

        for (var index = 0; index < outcomes.Length; index++)
        {
            if (outcomes[index]?.Report is { InProgressCount: > 0 })
            {
                pending.Add(index);
            }
        }

        return pending;
    }

    private static int CountInProgress(
        TenantReviewOutcome?[] outcomes,
        IReadOnlyList<int> indices)
    {
        return indices.Sum(
            index => outcomes[index]?.Report?.InProgressCount ?? 0
        );
    }

    private static string FormatDuration(
        TimeSpan duration)
    {
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}m {duration.Seconds}s"
                : $"{(int)duration.TotalSeconds}s";
    }

    private static void ValidateCandidates(
        IReadOnlyList<TenantDto> candidates)
    {
        var ids =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var tenant in candidates)
        {
            if (!string.Equals(
                tenant.Kind,
                "customer",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Scoped inventory candidates must be Acronis customer tenants.",
                    nameof(candidates)
                );
            }

            if (!Guid.TryParse(
                tenant.Id,
                out var parsedId))
            {
                throw new ArgumentException(
                    "Scoped inventory candidate has no valid Acronis tenant UUID.",
                    nameof(candidates)
                );
            }

            if (!ids.Add(
                parsedId.ToString("D")))
            {
                throw new ArgumentException(
                    "Scoped inventory candidates contain duplicate tenant UUIDs.",
                    nameof(candidates)
                );
            }
        }
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
        TenantBackupReviewFailure? Failure
    );
}
