using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Zendesk.Deletion.Deleting.Dtos;

/// <summary>
/// Contract of the bulk delete response and the job status endpoint.
/// Everything stays nullable: a missing value is unknown, never defaulted.
/// </summary>
public sealed class ZendeskJobStatusEnvelopeDto
{
    [JsonPropertyName("job_status")]
    public ZendeskJobStatusDto? JobStatus { get; init; }
}

public sealed class ZendeskJobStatusDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("progress")]
    public int? Progress { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("results")]
    public List<ZendeskJobResultDto>? Results { get; init; }
}

public sealed class ZendeskJobResultDto
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("success")]
    public bool? Success { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }
}
