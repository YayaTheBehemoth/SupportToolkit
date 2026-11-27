using System.Text.Json;
using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.Duplicati.Transport;
using SupportToolkit.Providers.Duplicati.Transport.Dtos;

namespace SupportToolkit.Providers.Duplicati.Jobs;

/// <summary>
/// Reads every backup job of one Duplicati server with its latest outcome.
/// Read-only; a server that cannot be read fails as a whole, never with a
/// partial job list.
/// </summary>
public sealed class DuplicatiServerReader
{
    /// <summary>
    /// Runs read per job: enough to find the latest run and, usually, the
    /// latest success; the newest backup version date covers the rest.
    /// </summary>
    public const int RunsPerJob = 20;

    private readonly HttpClient _httpClient;
    private readonly OperationalLogger? _logger;

    public DuplicatiServerReader(
        HttpClient httpClient,
        OperationalLogger? logger = null)
    {
        _httpClient =
            httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger;
    }

    public async Task<IReadOnlyList<DuplicatiJob>> ReadJobsAsync(
        ResolvedDuplicatiServer server,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        var client = new DuplicatiApiClient(_httpClient, server.BaseUri, _logger);

        await client.SignInAsync(server.Password, cancellationToken);

        var state = await client.GetServerStateAsync(cancellationToken);
        var runningId = state.ActiveTask?.BackupId;

        var jobs = new List<DuplicatiJob>();

        foreach (var entry in await client.GetBackupsAsync(cancellationToken))
        {
            var backup =
                entry.Backup
                ?? throw new InvalidOperationException(
                    "Duplicati listed a job without its settings.");

            if (string.IsNullOrWhiteSpace(backup.Id))
            {
                throw new InvalidOperationException(
                    "Duplicati listed a job without an ID.");
            }

            var log = await client.GetLogAsync(backup.Id, RunsPerJob, cancellationToken);

            jobs.Add(
                DuplicatiJobStatus.Evaluate(
                    backup,
                    ReadRuns(log),
                    string.Equals(runningId, backup.Id, StringComparison.Ordinal))
                with
                {
                    StoredBytes = DuplicatiJobStatus.StoredBytes(backup)
                });
        }

        return jobs;
    }

    /// <summary>
    /// The result entries of a job's log. An entry whose result cannot be
    /// read is kept as a result without a time, so it never counts as the
    /// latest run; the job's other evidence still decides.
    /// </summary>
    private static IReadOnlyList<DuplicatiRunResultDto> ReadRuns(
        IReadOnlyList<DuplicatiLogEntryDto> log)
    {
        var runs = new List<DuplicatiRunResultDto>();

        foreach (var entry in log)
        {
            if (!string.Equals(entry.Type, "Result", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(entry.Message))
            {
                continue;
            }

            try
            {
                if (JsonSerializer.Deserialize<DuplicatiRunResultDto>(entry.Message) is { } run)
                {
                    runs.Add(run);
                }
            }
            catch (JsonException)
            {
                // Not a result document; ignored like other log entries.
            }
        }

        return runs;
    }
}
