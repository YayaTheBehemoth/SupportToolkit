using System.Globalization;
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.Acronis.Usage.Dtos;

namespace SupportToolkit.Providers.Acronis.Usage;

/// <summary>
/// One usage measurement for one tenant, exactly as Acronis reported
/// it. The unit is carried as reported and never converted. Missing
/// values stay null.
/// </summary>
public sealed record AcronisTenantUsage(
    string TenantId,
    string? MetricName,
    string? Edition,
    string? Type,
    decimal? Value,
    decimal? AbsoluteValue,
    string? Unit,
    string? RangeStart);

/// <summary>
/// Usage per requested tenant. A requested tenant with no entry in
/// ByTenant had no usage returned: unknown, not zero.
/// </summary>
public sealed record AcronisUsageResult(
    IReadOnlyDictionary<string, IReadOnlyList<AcronisTenantUsage>> ByTenant,
    int UnrequestedItems);

/// <summary>
/// Read-only per-tenant usage through the batch usage endpoint.
///
/// Acronis documents the batch size limit nowhere, so requests are
/// chunked conservatively. Any failed chunk fails the whole read: a
/// partial result would look like tenants without usage.
/// </summary>
public sealed class AcronisUsageProvider
{
    public const int ChunkSize = 50;

    private const string Path = "/api/2/tenants/usages";

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisUsageProvider(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));

        _logger = logger;
    }

    public async Task<AcronisUsageResult> GetUsagesAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);

        var requested =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in tenantIds)
        {
            if (!Guid.TryParse(id, out var parsed))
            {
                throw new ArgumentException(
                    "Usage can only be requested for valid tenant UUIDs.",
                    nameof(tenantIds));
            }

            requested.Add(parsed.ToString("D"));
        }

        var byTenant =
            new Dictionary<string, List<AcronisTenantUsage>>(
                StringComparer.OrdinalIgnoreCase);

        var unrequested = 0;

        foreach (var chunk in requested.Chunk(ChunkSize))
        {
            var path =
                $"{Path}?tenants={string.Join(",", chunk)}";

            _logger?.Info(
                $"Fetching Acronis usage for {chunk.Length} tenants.");

            using var response =
                await _apiClient.GetAsync(path, cancellationToken);

            response.EnsureSuccessStatusCode();

            var page =
                await response.Content
                    .ReadFromJsonAsync<UsagePageDto>(
                        cancellationToken)
                ?? throw new InvalidOperationException(
                    "Acronis returned an empty usage response.");

            foreach (var item in page.Items)
            {
                if (!Guid.TryParse(item.Tenant, out var tenant)
                    || !requested.Contains(tenant.ToString("D")))
                {
                    unrequested++;
                    continue;
                }

                var key = tenant.ToString("D");

                if (!byTenant.TryGetValue(key, out var rows))
                {
                    rows = [];
                    byTenant[key] = rows;
                }

                rows.AddRange(
                    item.Usages.Select(
                        usage => new AcronisTenantUsage(
                            key,
                            FirstNonBlank(usage.UsageName, usage.Name),
                            usage.Edition,
                            usage.Type,
                            usage.Value,
                            usage.AbsoluteValue,
                            usage.MeasurementUnit,
                            usage.RangeStart)));
            }
        }

        _logger?.Info(
            "Acronis usage fetched for " +
            byTenant.Count.ToString(CultureInfo.InvariantCulture) +
            " tenants.");

        return new AcronisUsageResult(
            byTenant.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<AcronisTenantUsage>)pair.Value,
                StringComparer.OrdinalIgnoreCase),
            unrequested);
    }

    private static string? FirstNonBlank(
        string? first,
        string? second) =>
        !string.IsNullOrWhiteSpace(first)
            ? first
            : string.IsNullOrWhiteSpace(second)
                ? null
                : second;
}
