using System.Globalization;
using SupportToolkit.Providers.Duplicati.Transport.Dtos;

namespace SupportToolkit.Providers.Duplicati.Jobs;

/// <summary>The latest outcome of a Duplicati backup job.</summary>
public enum DuplicatiJobOutcome
{
    /// <summary>The job has never finished or failed a run.</summary>
    NeverRun,

    Success,

    /// <summary>Finished, with warnings (for example a missing source).</summary>
    Warning,

    /// <summary>Error or Fatal, or failed before it could log a result.</summary>
    Failed,

    /// <summary>What Duplicati reported does not decide it; see Detail.</summary>
    Unknown
}

/// <summary>
/// One backup job on one Duplicati server, as the toolkit reads it: no
/// destination, no error text.
/// </summary>
public sealed record DuplicatiJob(
    string Id,
    string Name,
    DuplicatiJobOutcome Outcome,
    DateTimeOffset? LastRun,
    DateTimeOffset? LastSuccess,
    bool IsRunning,
    string? Detail = null);

/// <summary>
/// Decides a job's latest outcome from what Duplicati reports, which no
/// single field states (lab/duplicati/README.md):
///
/// - the run log has each finished run's result, newest first, but a run
///   that fails before the job's local database exists is not logged;
/// - the metadata keeps the last error's date even after later successes,
///   with whole-second precision;
/// - a warning appears only in the log.
///
/// So the newest of the last logged run and the last error decides. When
/// the two fall in the same second and disagree, or anything cannot be
/// read, the outcome is Unknown rather than a guess.
/// </summary>
public static class DuplicatiJobStatus
{
    private const string MetadataTimeFormat = "yyyyMMdd'T'HHmmss'Z'";

    internal static DuplicatiJob Evaluate(
        DuplicatiBackupDto backup,
        IReadOnlyList<DuplicatiRunResultDto> runsNewestFirst,
        bool isRunning)
    {
        var id = backup.Id ?? string.Empty;
        var name = string.IsNullOrWhiteSpace(backup.Name) ? $"Job {id}" : backup.Name.Trim();
        var metadata = backup.Metadata ?? [];

        if (!TryReadTime(metadata, "LastErrorDate", out var lastError)
            || !TryReadTime(metadata, "LastBackupFinished", out var lastFinished)
            || !TryReadTime(metadata, "LastBackupDate", out var lastVersion))
        {
            return new DuplicatiJob(
                id, name, DuplicatiJobOutcome.Unknown, null, null, isRunning,
                "Duplicati reported a timestamp in an unexpected format.");
        }

        var backupRuns =
            runsNewestFirst
                .Where(run =>
                    run.EndTime is not null
                    && (run.MainOperation is null
                        || string.Equals(run.MainOperation, "Backup", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(run => run.EndTime)
                .ToArray();

        var latest = backupRuns.FirstOrDefault();

        var lastSuccess =
            backupRuns
                .FirstOrDefault(run => ResultOf(run) is DuplicatiJobOutcome.Success or DuplicatiJobOutcome.Warning)
                ?.EndTime
            ?? lastVersion;

        if (latest is null && lastFinished is null && lastError is null)
        {
            return new DuplicatiJob(
                id, name, DuplicatiJobOutcome.NeverRun, null, null, isRunning);
        }

        DuplicatiJob Job(DuplicatiJobOutcome outcome, DateTimeOffset? lastRun, string? detail = null) =>
            new(id, name, outcome, lastRun, lastSuccess, isRunning, detail);

        var latestEnd = latest?.EndTime is { } end ? Seconds(end) : (DateTimeOffset?)null;

        if (latestEnd is null)
        {
            // Metadata says a run finished, but the log shows none.
            return lastError is not null && lastFinished is null
                ? Job(DuplicatiJobOutcome.Failed, lastError)
                : Job(
                    DuplicatiJobOutcome.Unknown,
                    lastFinished ?? lastError,
                    "Duplicati reports a finished run that its log does not show.");
        }

        if (lastFinished > latestEnd)
        {
            return Job(
                DuplicatiJobOutcome.Unknown,
                lastFinished,
                "Duplicati reports a newer run than its log shows.");
        }

        var logged = ResultOf(latest!);

        if (lastError > latestEnd)
        {
            return Job(DuplicatiJobOutcome.Failed, lastError);
        }

        if (lastError == latestEnd && logged != DuplicatiJobOutcome.Failed)
        {
            return Job(
                DuplicatiJobOutcome.Unknown,
                latest!.EndTime,
                "The last error and the last logged run fall in the same " +
                "second, so which came last cannot be told.");
        }

        return logged == DuplicatiJobOutcome.Unknown
            ? Job(
                DuplicatiJobOutcome.Unknown,
                latest!.EndTime,
                "Duplicati reported a result the toolkit does not know.")
            : Job(logged, latest!.EndTime);
    }

    private static DuplicatiJobOutcome ResultOf(DuplicatiRunResultDto run)
    {
        if (run.Interrupted)
        {
            return DuplicatiJobOutcome.Failed;
        }

        return run.ParsedResult?.Trim().ToLowerInvariant() switch
        {
            "success" => DuplicatiJobOutcome.Success,
            "warning" => DuplicatiJobOutcome.Warning,
            "error" or "fatal" => DuplicatiJobOutcome.Failed,
            _ => DuplicatiJobOutcome.Unknown
        };
    }

    private static DateTimeOffset Seconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Offset);

    /// <summary>
    /// A missing or empty value is null and readable; a value in another
    /// format is unreadable.
    /// </summary>
    private static bool TryReadTime(
        IReadOnlyDictionary<string, string?> metadata,
        string key,
        out DateTimeOffset? value)
    {
        value = null;

        if (!metadata.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (DateTimeOffset.TryParseExact(
                text.Trim(),
                MetadataTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
