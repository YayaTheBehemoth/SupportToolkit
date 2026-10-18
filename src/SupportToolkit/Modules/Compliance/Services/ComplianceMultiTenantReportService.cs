
using System.Net.Http;
using System.Text.Json;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance.Services;

public sealed record ComplianceTenantScope(
    string Key,
    string DisplayName
);

/// <summary>
/// One explicitly scoped pair of data providers.
///
/// Production tenant acquisition is deliberately out of scope.
/// </summary>
public sealed record ComplianceTenantSource(
    ComplianceTenantScope Scope,
    IGraphManagedDeviceProvider DeviceProvider,
    IGraphComplianceSettingProvider SettingProvider
);

public sealed record ComplianceTenantCollection(
    ComplianceTenantScope Scope,
    ComplianceDeviceListResult? Data,
    string? FailureReason
)
{
    public bool Succeeded => Data is not null;
}

public sealed record ComplianceMultiTenantReport(
    IReadOnlyList<ComplianceTenantCollection> Tenants
)
{
    public int SuccessfulTenants =>
        Tenants.Count(tenant => tenant.Succeeded);

    public int FailedTenants =>
        Tenants.Count - SuccessfulTenants;

    // This does not mean that the underlying API data is complete.
    public bool AllSelectedCollectionsSucceeded =>
        Tenants.Count > 0 && FailedTenants == 0;
}

/// <summary>
/// Collects independently scoped tenant evidence.
///
/// Expected collection failures are isolated to their tenant.
/// Cancellation of the overall operation still propagates.
///
/// Failure details are deliberately sanitized for report output.
/// </summary>
public sealed class ComplianceMultiTenantReportService
{
    public async Task<ComplianceMultiTenantReport> CollectAsync(
        IReadOnlyList<ComplianceTenantSource> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);

        ValidateSources(sources);

        var results = new List<ComplianceTenantCollection>();

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // A new list service receives only the providers
                // belonging to the current tenant scope.
                var collector = new ComplianceDeviceListService(
                    source.DeviceProvider,
                    source.SettingProvider
                );

                var data = await collector.GetAsync(
                    cancellationToken
                );

                results.Add(
                    new ComplianceTenantCollection(
                        source.Scope,
                        data,
                        null
                    )
                );
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // User-requested cancellation is not a failed tenant.
                throw;
            }
            catch (Exception exception)
            {
                // Do not expose arbitrary exception messages.
                // They could include URLs or internal details.
                results.Add(
                    new ComplianceTenantCollection(
                        source.Scope,
                        null,
                        DescribeFailure(exception)
                    )
                );
            }
        }

        return new ComplianceMultiTenantReport(
            results.AsReadOnly()
        );
    }

    private static void ValidateSources(
        IReadOnlyList<ComplianceTenantSource> sources)
    {
        if (sources.Count == 0)
        {
            throw new ArgumentException(
                "At least one explicit tenant scope is required.",
                nameof(sources)
            );
        }

        var keys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var source in sources)
        {
            if (source is null
                || source.Scope is null
                || string.IsNullOrWhiteSpace(source.Scope.Key)
                || string.IsNullOrWhiteSpace(
                    source.Scope.DisplayName)
                || source.DeviceProvider is null
                || source.SettingProvider is null)
            {
                throw new ArgumentException(
                    "Each tenant must have a key, display name " +
                    "and its own provider pair.",
                    nameof(sources)
                );
            }

            if (!keys.Add(source.Scope.Key.Trim()))
            {
                throw new InvalidDataException(
                    "Duplicate tenant scope key. " +
                    "Consolidated collection was not started."
                );
            }
        }
    }

    private static string DescribeFailure(Exception exception)
    {
        return exception switch
        {
            HttpRequestException http
                when http.StatusCode.HasValue =>
                $"HTTP {(int)http.StatusCode.Value}",

            HttpRequestException =>
                "HTTP connectivity failure",

            TaskCanceledException =>
                "Collection timed out or was canceled",

            InvalidDataException =>
                "Invalid or inconsistent collection data",

            JsonException =>
                "Unexpected response data",

            InvalidOperationException =>
                "Collection validation failure",

            _ =>
                "Unexpected collection failure"
        };
    }
}
