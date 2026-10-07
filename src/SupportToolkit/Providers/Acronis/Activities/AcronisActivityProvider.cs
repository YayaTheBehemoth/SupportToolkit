using System.Globalization;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Activities.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Providers.Acronis.Activities;

/// <summary>
/// Provides read-only access to the Acronis Task Manager Activities API.
/// </summary>
public sealed class AcronisActivityProvider
{
    private const int PageSize =
        100;

    private const int MaxPages =
        100;

    private const int MaxActivities =
        10_000;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisActivityProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient;

        _logger =
            logger;
    }

    /// <summary>
    /// Fetches backup activities that started inside the requested
    /// half-open interval:
    ///
    ///     fromInclusive <= startedAt < toExclusive
    ///
    /// No lifecycle-state filtering is applied here.
    /// State belongs to downstream classification.
    /// </summary>
    public async Task<IReadOnlyList<AcronisActivityDto>>
        GetBackupActivitiesForTenantAsync(
            string tenantId,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(
                tenantId,
                out _))
        {
            throw new ArgumentException(
                "A tenant UUID is required for a " +
                "customer-scoped activities request.",
                nameof(tenantId)
            );
        }

        if (toExclusive <= fromInclusive)
        {
            throw new ArgumentException(
                "The activity interval end must be later " +
                "than the interval start.",
                nameof(toExclusive)
            );
        }

        _logger?.Info(
            "Fetching customer-scoped backup activities."
        );

        _logger?.Info(
            $"Activity interval: " +
            $"{fromInclusive:O} -> {toExclusive:O}."
        );

        var scopedAccessToken =
            await _apiClient
                .GetScopedAccessTokenAsync(
                    tenantId,
                    cancellationToken
                );

        var startedAtFilter =
            $"ge({FormatApiTimestamp(fromInclusive)})";

        string? requestPath =
            "/api/task_manager/v2/activities" +
            "?policyType=backup" +
            $"&startedAt={Uri.EscapeDataString(startedAtFilter)}" +
            $"&order={Uri.EscapeDataString("asc(startedAt)")}" +
            $"&limit={PageSize}";

        var activities =
            new List<AcronisActivityDto>();

        var pageNumber =
            1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Acronis activity pagination exceeded " +
                    $"{MaxPages} pages."
                );
            }

            _logger?.Info(
                $"Fetching backup activity page {pageNumber}."
            );

            using var response =
                await _apiClient
                    .GetWithAccessTokenAsync(
                        requestPath,
                        scopedAccessToken,
                        cancellationToken
                    );

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<AcronisActivityPageDto>(
                        cancellationToken:
                            cancellationToken
                    )
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty activities response."
                );

            _logger?.Info(
                $"Backup activity page {pageNumber}: " +
                $"{page.Items.Count} item(s)."
            );

            var reachedIntervalEnd =
                false;

            foreach (var activity
                     in page.Items)
            {
                if (activity.StartedAt is null)
                {
                    /*
                     * Keep unexplained rows.
                     *
                     * Downstream code must decide that they are Unknown
                     * rather than the provider silently throwing them away.
                     */
                    activities.Add(
                        activity
                    );

                    continue;
                }

                if (activity.StartedAt.Value
                    >= toExclusive)
                {
                    reachedIntervalEnd =
                        true;

                    break;
                }

                if (activity.StartedAt.Value
                    < fromInclusive)
                {
                    continue;
                }

                activities.Add(
                    activity
                );

                if (activities.Count
                    > MaxActivities)
                {
                    throw new InvalidOperationException(
                        $"Acronis returned more than " +
                        $"{MaxActivities} backup activities for one " +
                        "tenant interval. Aborting as a safety guard."
                    );
                }
            }

            if (reachedIntervalEnd)
            {
                _logger?.Info(
                    "Requested activity interval end reached."
                );

                break;
            }

            var after =
                page.Paging.Cursors.After;

            if (string.IsNullOrWhiteSpace(
                    after))
            {
                break;
            }

            requestPath =
                "/api/task_manager/v2/activities" +
                $"?limit={PageSize}" +
                $"&after={Uri.EscapeDataString(after)}";

            pageNumber++;
        }

        _logger?.Info(
            $"Backup activity fetch completed: " +
            $"{activities.Count} activity/activities across " +
            $"{pageNumber} page(s)."
        );

        return activities.AsReadOnly();
    }

    private static string FormatApiTimestamp(
        DateTimeOffset timestamp)
    {
        return timestamp
            .ToUniversalTime()
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture
            );
    }
}