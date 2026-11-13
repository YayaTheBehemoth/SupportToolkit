using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Modules.UsageReport.Fixtures;

/// <summary>
/// Fixture mode: synthetic data, always available, contacts nothing.
/// </summary>
public sealed class FixtureUsageReportSetup : IUsageReportSetup
{
    public Task<SupportToolkitMode> GetModeAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(SupportToolkitMode.Fixture);

    public Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new UsageReportAvailability(SupportToolkitMode.Fixture, true, null));

    public Task<UsageReportRun> OpenAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(
            new UsageReportRun(
                SupportToolkitMode.Fixture,
                FixtureUsageReportSources.BackupItems,
                new FixtureUsageReportSources()));
}
