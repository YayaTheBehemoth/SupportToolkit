using SupportToolkit.Providers.Zendesk.Deletion.Deleting.Dtos;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Providers.Zendesk.Deletion.Deleting;

/// <summary>
/// What a deletion job reported for one ticket. Success is null when the
/// job did not say.
/// </summary>
public sealed record ZendeskDeleteJobResult(
    long TicketId,
    bool? Success,
    string? Status);

/// <summary>
/// A bulk deletion job as Zendesk reports it. Zendesk deletes
/// asynchronously: the request only queues a job, and the outcome is read
/// from the job's status.
/// </summary>
public sealed record ZendeskDeleteJob(
    string JobId,
    string Status,
    int? Progress,
    int? Total,
    string? Message,
    IReadOnlyList<ZendeskDeleteJobResult> Results)
{
    public const string Queued = "queued";
    public const string Working = "working";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public bool IsFinished =>
        Status is Completed or Failed;

    public bool IsRunning =>
        Status is Queued or Working;
}

/// <summary>
/// Ticket deletion through the bulk endpoint, and the status of its job.
///
/// It sends exactly the confirmed IDs it is given, at most 100 at a time,
/// and never retries: a request whose outcome is unclear must be reported
/// as unknown, not repeated. It must be built on the deletion pipeline;
/// the read-only pipeline blocks the request.
/// </summary>
public sealed class ZendeskTicketDeletionProvider
{
    public const int MaxIdsPerRequest =
        ZendeskTicketDeleteHandler.MaxIdsPerRequest;

    private readonly ZendeskDeletionApiClient _client;

    public ZendeskTicketDeletionProvider(
        ZendeskDeletionApiClient client)
    {
        _client =
            client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ZendeskDeleteJob> StartDeleteAsync(
        IReadOnlyList<long> ticketIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticketIds);

        if (ticketIds.Count is < 1 or > MaxIdsPerRequest
            || ticketIds.Any(id => id <= 0)
            || ticketIds.Distinct().Count() != ticketIds.Count)
        {
            throw new ArgumentException(
                $"Between 1 and {MaxIdsPerRequest} distinct, positive " +
                "ticket IDs are required.",
                nameof(ticketIds));
        }

        var dto =
            await _client.DeleteJsonAsync<ZendeskJobStatusEnvelopeDto>(
                "/api/v2/tickets/destroy_many.json?ids=" +
                string.Join(",", ticketIds),
                cancellationToken);

        return ToJob(dto);
    }

    public async Task<ZendeskDeleteJob> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId)
            || jobId.Length > 64
            || !jobId.All(
                character => char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_'))
        {
            throw new ArgumentException(
                "The job ID is not valid.",
                nameof(jobId));
        }

        var dto =
            await _client.GetJsonAsync<ZendeskJobStatusEnvelopeDto>(
                $"/api/v2/job_statuses/{jobId}.json",
                cancellationToken);

        return ToJob(dto);
    }

    private static ZendeskDeleteJob ToJob(
        ZendeskJobStatusEnvelopeDto envelope)
    {
        var job = envelope.JobStatus;

        if (job is null || string.IsNullOrWhiteSpace(job.Id))
        {
            throw new InvalidOperationException(
                "Zendesk did not return a job for the deletion.");
        }

        return new ZendeskDeleteJob(
            job.Id.Trim(),
            (job.Status ?? string.Empty).Trim().ToLowerInvariant(),
            job.Progress,
            job.Total,
            job.Message,
            (job.Results ?? [])
                .Where(result => result.Id is > 0)
                .Select(
                    result => new ZendeskDeleteJobResult(
                        result.Id!.Value,
                        result.Success,
                        result.Status))
                .ToArray());
    }
}
