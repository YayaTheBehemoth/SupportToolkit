namespace SupportToolkit.Modules.UsageReport.Services;

/// <summary>
/// The usage report an operator is working with, held in memory only:
/// nothing is written anywhere, and a new run or leaving the program
/// discards it. Any interface (console, web page, a later automated run)
/// drives the same session.
///
/// A run that fails leaves no current report, so a stale one is never
/// shown as if it were current. Not thread-safe: one session belongs to
/// one operator.
/// </summary>
public sealed class UsageReportSession
{
    private readonly UsageReportWorkflow _workflow;

    public UsageReportSession(UsageReportWorkflow workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    /// <summary>The last report generated, or null.</summary>
    public Models.UsageReport? CurrentReport { get; private set; }

    public Task<UsageReportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        _workflow.GetAvailabilityAsync(cancellationToken);

    public async Task<Models.UsageReport> GenerateAsync(
        CancellationToken cancellationToken = default)
    {
        CurrentReport = null;

        var report = await _workflow.GenerateAsync(cancellationToken);

        CurrentReport = report;

        return report;
    }
}
