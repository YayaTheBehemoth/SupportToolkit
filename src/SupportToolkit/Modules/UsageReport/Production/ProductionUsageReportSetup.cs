using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Modules.UsageReport.Production;

/// <summary>
/// Production mode: the profile's backup and commercial systems, read-only
/// (UsageReportSystems; without one, Acronis and Business Central).
/// Local configuration is checked before any sign-in or request: the
/// backup items, then the backup system, then the commercial system.
/// </summary>
public sealed class ProductionUsageReportSetup : IUsageReportSetup
{
    private const string NoItemsReason =
        "No backup items are configured for the usage report " +
        "(monitoringScope.usage-report.serviceItemNumbers in the active profile).";

    private readonly SupportToolkitConfigurationResolver _resolver;
    private readonly UsageReportSystems _systems;
    private readonly Func<CancellationToken, Task<IUsageReportSources>> _sources;

    /// <param name="sources">
    /// Replaces both halves when given (tests and synthetic runs).
    /// </param>
    /// <param name="systems">
    /// The profile's systems; null uses Acronis and Business Central.
    /// </param>
    public ProductionUsageReportSetup(
        SupportToolkitConfigurationResolver resolver,
        Func<CancellationToken, Task<IUsageReportSources>>? sources = null,
        UsageReportSystems? systems = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        _systems = systems ?? ProductionUsageReportSources.Systems(_resolver);

        _sources = sources ?? OpenSourcesAsync;
    }

    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Production);

    public async Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var mode = SupportToolkitMode.Production;

        UsageSystemNames? names = null;

        try
        {
            names =
                _systems.Names is { } lookup
                    ? await lookup(cancellationToken)
                    : null;
        }
        catch (InvalidOperationException)
        {
            // Unknown systems are reported by the checks below.
        }

        try
        {
            if ((await BackupItemsAsync(cancellationToken)).Count == 0)
            {
                return new UsageReportAvailability(
                    mode,
                    false,
                    NoItemsReason)
                {
                    Systems = names
                };
            }

            await _systems.CheckBackup(cancellationToken);
            await _systems.CheckCommercial(cancellationToken);

            return new UsageReportAvailability(mode, true, null)
            {
                Systems = names
            };
        }
        catch (InvalidOperationException exception)
        {
            return new UsageReportAvailability(mode, false, exception.Message)
            {
                Systems = names
            };
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

    /// <summary>
    /// The commercial half, then the backup half, as the first systems
    /// were always opened. A backup half that fails releases the other.
    /// </summary>
    private async Task<IUsageReportSources> OpenSourcesAsync(
        CancellationToken cancellationToken)
    {
        var commercial = await _systems.OpenCommercial(cancellationToken);

        try
        {
            return new CombinedUsageReportSources(
                commercial,
                await _systems.OpenBackup(cancellationToken));
        }
        catch
        {
            commercial.Dispose();
            throw;
        }
    }

    private Task<IReadOnlyList<string>> BackupItemsAsync(
        CancellationToken cancellationToken) =>
        _resolver.GetServiceItemNumbersAsync(
            UsageReportModule.ModuleCommand,
            cancellationToken);
}
