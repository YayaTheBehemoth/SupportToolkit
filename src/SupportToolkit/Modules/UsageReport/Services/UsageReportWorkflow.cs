using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Fixtures;
using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Modules.UsageReport.Production;

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
/// Fixture mode runs on synthetic data and contacts nothing. Production
/// reads Business Central (interactive sign-in) and Acronis, read-only,
/// through the active profile's connections. Local configuration is
/// checked before any sign-in or request.
/// </summary>
public sealed class UsageReportWorkflow
{
    private readonly SupportToolkitConfigurationResolver _resolver;
    private readonly Action<string>? _progress;
    private readonly Func<CancellationToken, Task<IUsageReportSources>> _productionSources;
    private readonly TimeProvider _timeProvider;

    public UsageReportWorkflow(
        SupportToolkitConfigurationResolver resolver,
        Action<string>? progress = null,
        Func<CancellationToken, Task<IUsageReportSources>>? productionSources = null,
        TimeProvider? timeProvider = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _progress = progress;

        _productionSources =
            productionSources
            ?? (token => ProductionUsageReportSources.CreateAsync(_resolver, token));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken = default) =>
        _resolver.GetModuleModeAsync(UsageReportModule.ModuleCommand, cancellationToken);

    /// <summary>
    /// Local checks only. Any configuration problem makes the report
    /// unavailable, with the reason.
    /// </summary>
    public async Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        var mode = await GetModeAsync(cancellationToken);

        if (mode == SupportToolkitMode.Fixture)
        {
            return new UsageReportAvailability(mode, true, null);
        }

        try
        {
            if ((await BackupItemsAsync(cancellationToken)).Count == 0)
            {
                return new UsageReportAvailability(
                    mode,
                    false,
                    NoItemsReason);
            }

            await _resolver.GetAcronisConnectionAsync(cancellationToken);
            await _resolver.GetBusinessCentralConnectionAsync(cancellationToken);

            return new UsageReportAvailability(mode, true, null);
        }
        catch (InvalidOperationException exception)
        {
            return new UsageReportAvailability(mode, false, exception.Message);
        }
    }

    public async Task<Models.UsageReport> GenerateAsync(
        CancellationToken cancellationToken = default)
    {
        var mode = await GetModeAsync(cancellationToken);

        IReadOnlyList<string> items;
        IUsageReportSources sources;

        if (mode == SupportToolkitMode.Fixture)
        {
            items = FixtureUsageReportSources.BackupItems;
            sources = new FixtureUsageReportSources();
        }
        else
        {
            items = await BackupItemsAsync(cancellationToken);

            if (items.Count == 0)
            {
                throw new InvalidOperationException(NoItemsReason);
            }

            sources = await _productionSources(cancellationToken);
        }

        using (sources)
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
                mode);
        }
    }

    private const string NoItemsReason =
        "No backup items are configured for the usage report " +
        "(monitoringScope.usage-report.serviceItemNumbers in the active profile).";

    private Task<IReadOnlyList<string>> BackupItemsAsync(
        CancellationToken cancellationToken) =>
        _resolver.GetServiceItemNumbersAsync(
            UsageReportModule.ModuleCommand,
            cancellationToken);

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
