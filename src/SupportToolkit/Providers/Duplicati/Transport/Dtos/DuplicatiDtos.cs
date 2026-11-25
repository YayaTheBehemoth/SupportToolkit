using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Duplicati.Transport.Dtos;

/*
 * Shapes observed against Duplicati 2.4.1.0 (lab/duplicati/README.md).
 *
 * Deliberately incomplete: a job's TargetURL can hold destination
 * credentials and its error messages hold paths, so neither has a property
 * here and neither is ever kept.
 */

internal sealed class DuplicatiLoginRequestDto
{
    [JsonPropertyName("Password")]
    public required string Password { get; init; }

    [JsonPropertyName("RememberMe")]
    public bool RememberMe { get; init; }
}

internal sealed class DuplicatiLoginResponseDto
{
    [JsonPropertyName("AccessToken")]
    public string? AccessToken { get; init; }
}

internal sealed class DuplicatiBackupEntryDto
{
    [JsonPropertyName("Backup")]
    public DuplicatiBackupDto? Backup { get; init; }
}

internal sealed class DuplicatiBackupDto
{
    [JsonPropertyName("ID")]
    public string? Id { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>
    /// Only the named timestamps are read (DuplicatiJobMetadata);
    /// LastErrorMessage is never kept.
    /// </summary>
    [JsonPropertyName("Metadata")]
    public Dictionary<string, string?>? Metadata { get; init; }
}

internal sealed class DuplicatiLogEntryDto
{
    [JsonPropertyName("ID")]
    public long Id { get; init; }

    [JsonPropertyName("Type")]
    public string? Type { get; init; }

    /// <summary>A JSON document of its own: DuplicatiRunResultDto.</summary>
    [JsonPropertyName("Message")]
    public string? Message { get; init; }
}

internal sealed class DuplicatiRunResultDto
{
    [JsonPropertyName("MainOperation")]
    public string? MainOperation { get; init; }

    [JsonPropertyName("ParsedResult")]
    public string? ParsedResult { get; init; }

    [JsonPropertyName("Interrupted")]
    public bool Interrupted { get; init; }

    [JsonPropertyName("BeginTime")]
    public DateTimeOffset? BeginTime { get; init; }

    [JsonPropertyName("EndTime")]
    public DateTimeOffset? EndTime { get; init; }
}

internal sealed class DuplicatiServerStateDto
{
    [JsonPropertyName("ActiveTask")]
    public DuplicatiActiveTaskDto? ActiveTask { get; init; }
}

internal sealed class DuplicatiActiveTaskDto
{
    /// <summary>The ID of the job the active task runs.</summary>
    [JsonPropertyName("Item2")]
    public string? BackupId { get; init; }
}
