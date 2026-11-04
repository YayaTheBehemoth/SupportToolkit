using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>
/// Where one usage report run gets its data. The host chooses the
/// implementation (synthetic or the real systems) for each run, so the
/// workflow never decides the mode or reads a connection itself.
/// </summary>
public interface IUsageReportSetup
{
    Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Local checks only. Any configuration problem makes the report
    /// unavailable, with the reason.
    /// </summary>
    Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// The backup items and the sources for one run. Local configuration
    /// is checked first; creating the sources signs in to nothing.
    /// </summary>
    Task<UsageReportRun> OpenAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// One run's mode, backup items and sources. The caller disposes the
/// sources when the run is over.
/// </summary>
public sealed record UsageReportRun(
    SupportToolkitMode Mode,
    IReadOnlyList<string> BackupItems,
    IUsageReportSources Sources);
