
using SupportToolkit.Modules.Compliance.Services;

namespace SupportToolkit.Modules.Compliance.Reporting;

/// <summary>
/// A point-in-time presentation of collected investigation evidence.
///
/// This does not represent a compliance case, remediation,
/// verification, or closure decision.
/// </summary>
public sealed record ComplianceInvestigationReport(
    ComplianceEvidenceInvestigation Investigation,
    DateTimeOffset GeneratedAtUtc,
    TimeSpan? TimeSinceLastCheckIn
);

public sealed class ComplianceInvestigationReportFactory
{
    private readonly TimeProvider _timeProvider;

    public ComplianceInvestigationReportFactory(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ComplianceInvestigationReport Create(
        ComplianceEvidenceInvestigation investigation)
    {
        ArgumentNullException.ThrowIfNull(investigation);

        var now = _timeProvider.GetUtcNow();

        var lastSync = investigation.Device.LastSyncDateTime;

        TimeSpan? age = null;

        // A future timestamp is inconsistent with the
        // report clock. Never present it as a negative age.
        if (lastSync.HasValue && lastSync.Value <= now)
        {
            age = now - lastSync.Value;
        }

        return new ComplianceInvestigationReport(
            investigation,
            now,
            age
        );
    }
}
