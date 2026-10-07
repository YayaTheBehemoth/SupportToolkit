using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.Activities.Dtos;

public sealed class AcronisActivityPageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisActivityDto> Items { get; init; } =
        [];

    [JsonPropertyName("paging")]
    public AcronisActivityPagingDto Paging { get; init; } =
        new();
}

public sealed class AcronisActivityPagingDto
{
    [JsonPropertyName("cursors")]
    public AcronisActivityCursorsDto Cursors { get; init; } =
        new();
}

public sealed class AcronisActivityCursorsDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}
