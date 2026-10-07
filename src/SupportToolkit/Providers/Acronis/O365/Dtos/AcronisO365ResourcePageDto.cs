using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Acronis.O365.Dtos;

public sealed class AcronisO365ResourcePageDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AcronisO365ResourceDto> Items { get; init; } =
        [];

    [JsonPropertyName("paging")]
    public AcronisO365PagingDto Paging { get; init; } =
        new();

    [JsonPropertyName("timeStamp")]
    public long? TimeStamp { get; init; }
}

public sealed class AcronisO365PagingDto
{
    [JsonPropertyName("cursors")]
    public AcronisO365CursorDto Cursors { get; init; } =
        new();
}

public sealed class AcronisO365CursorDto
{
    [JsonPropertyName("after")]
    public string? After { get; init; }

    [JsonPropertyName("before")]
    public string? Before { get; init; }
}