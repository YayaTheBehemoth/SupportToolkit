using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;

namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>
/// Whether the usage report can be generated, and in which mode. Reason
/// says what is missing when it cannot.
/// </summary>
public sealed record UsageReportAvailability(
    SupportToolkitMode Mode,
    bool IsAvailable,
    string? Reason);

/// <summary>
/// Generates a usage report: Acronis storage per customer next to what
/// Business Central bills for the backup items. It contains no console or
/// user-interface code, and stores nothing.
///
/// Where the data comes from is the setup's business: the host chooses
/// synthetic data or the real systems (read-only) for each run.
/// </summary>
public sealed class UsageReportWorkflow
{
    private readonly IUsageReportSetup _setup;
    private readonly Action<string>? _progress;
    private readonly TimeProvider _timeProvider;

    public UsageReportWorkflow(
        IUsageReportSetup setup,
        Action<string>? progress = null,
        TimeProvider? timeProvider = null)
    {
        _setup = setup ?? throw new ArgumentNullException(nameof(setup));
        _progress = progress;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken = default) =>
        _setup.GetModeAsync(cancellationToken);

    /// <summary>
    /// Local checks only. Any configuration problem makes the report
    /// unavailable, with the reason.
    /// </summary>
    public Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        _setup.GetAvailabilityAsync(cancellationToken);

    public async Task<Models.UsageReport> GenerateAsync(
        CancellationToken cancellationToken = default)
    {
        var run = await _setup.OpenAsync(cancellationToken);
        var items = run.BackupItems;

        using (var sources = run.Sources)
        {
            _progress?.Invoke("Reading subscriptions...");

            var headers = await sources.GetSubscriptionHeadersAsync(cancellationToken);
            var billing = await sources.GetBillingLinesAsync(cancellationToken);

            var customers =
                BackupCustomers(headers, billing, items);

            var names =
                await sources.GetCustomerPartiesAsync(customers, cancellationToken);

            _progress?.Invoke("Reading tenants...");

            var tenants = await sources.GetCustomerTenantsAsync(cancellationToken);

            var matching =
                await new AutoCustomerMappingResolver()
                    .ResolveAsync(
                        tenants.Tenants,
                        names.Parties,
                        cancellationToken: cancellationToken);

            _progress?.Invoke($"Reading usage for {tenants.Tenants.Count} tenants...");

            var storage =
                await sources.GetStorageAsync(
                    tenants.Tenants.Select(tenant => tenant.Id).ToArray(),
                    cancellationToken);

            return UsageReportBuilder.Build(
                new UsageReportInputs(
                    headers,
                    billing,
                    items,
                    names,
                    tenants,
                    matching,
                    storage),
                _timeProvider.GetUtcNow(),
                run.Mode);
        }
    }

    /// <summary>
    /// Customers with a line for a backup item, whatever its status, so a
    /// customer whose only backup line is not active still appears.
    /// </summary>
    private static string[] BackupCustomers(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SubscriptionBillingLine> billing,
        IReadOnlyCollection<string> items)
    {
        var itemSet = SubscriptionItemNumbers.ToSet(items);

        var subscriptions =
            billing
                .Where(line => !string.IsNullOrWhiteSpace(line.SubscriptionNumber)
                    && itemSet.Contains(SubscriptionItemNumbers.Normalize(line.ItemNumber ?? string.Empty)))
                .Select(line => line.SubscriptionNumber!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return headers
            .Where(header => subscriptions.Contains(header.SubscriptionNumber.Trim())
                && !string.IsNullOrWhiteSpace(header.CustomerNumber))
            .Select(header => header.CustomerNumber!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
