using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Modules.UsageReport.Production;

/// <summary>
/// Production mode: Business Central (interactive sign-in) and Acronis,
/// read-only, through the active profile's connections. Local
/// configuration is checked before any sign-in or request.
/// </summary>
public sealed class ProductionUsageReportSetup : IUsageReportSetup
{
    private const string NoItemsReason =
        "No backup items are configured for the usage report " +
        "(monitoringScope.usage-report.serviceItemNumbers in the active profile).";

    private readonly SupportToolkitConfigurationResolver _resolver;
    private readonly Func<CancellationToken, Task<IUsageReportSources>> _sources;

    public ProductionUsageReportSetup(
        SupportToolkitConfigurationResolver resolver,
        Func<CancellationToken, Task<IUsageReportSources>>? sources = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        _sources =
            sources
            ?? (token => ProductionUsageReportSources.CreateAsync(_resolver, token));
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Production);

    public async Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var mode = SupportToolkitMode.Production;

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

    public async Task<UsageReportRun> OpenAsync(
        CancellationToken cancellationToken)
    {
        var items = await BackupItemsAsync(cancellationToken);

        if (items.Count == 0)
        {
            throw new InvalidOperationException(NoItemsReason);
        }

        return new UsageReportRun(
            SupportToolkitMode.Production,
            items,
            await _sources(cancellationToken));
    }

    private Task<IReadOnlyList<string>> BackupItemsAsync(
        CancellationToken cancellationToken) =>
        _resolver.GetServiceItemNumbersAsync(
            UsageReportModule.ModuleCommand,
            cancellationToken);
}
