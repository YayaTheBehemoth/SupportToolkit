using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.Dolibarr.Transport.Dtos;

/*
 * Shapes observed against Dolibarr 23.0.4 (lab/dolibarr/README.md). Only
 * the fields the toolkit uses. Dolibarr's types are loose: IDs, statuses
 * and quantities are strings, and a date is Unix seconds, "" or null.
 */

internal sealed class DolibarrContractDto
{
    [JsonPropertyName("id"), JsonConverter(typeof(LooseStringConverter))]
    public string? Id { get; init; }

    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    [JsonPropertyName("socid"), JsonConverter(typeof(LooseStringConverter))]
    public string? CustomerId { get; init; }

    /// <summary>0 draft, 1 validated, 2 closed.</summary>
    [JsonPropertyName("statut"), JsonConverter(typeof(LooseStringConverter))]
    public string? Status { get; init; }

    [JsonPropertyName("lines")]
    public List<DolibarrContractLineDto>? Lines { get; init; }
}

internal sealed class DolibarrContractLineDto
{
    [JsonPropertyName("id"), JsonConverter(typeof(LooseStringConverter))]
    public string? Id { get; init; }

    [JsonPropertyName("product_ref")]
    public string? ProductRef { get; init; }

    /// <summary>0 never activated, 4 running, 5 closed.</summary>
    [JsonPropertyName("statut"), JsonConverter(typeof(LooseStringConverter))]
    public string? Status { get; init; }

    [JsonPropertyName("qty"), JsonConverter(typeof(LooseStringConverter))]
    public string? Quantity { get; init; }

    [JsonPropertyName("date_start"), JsonConverter(typeof(LooseUnixTimeConverter))]
    public LooseTime PlannedStart { get; init; }

    [JsonPropertyName("date_end"), JsonConverter(typeof(LooseUnixTimeConverter))]
    public LooseTime PlannedEnd { get; init; }

    [JsonPropertyName("date_start_real"), JsonConverter(typeof(LooseUnixTimeConverter))]
    public LooseTime ActualStart { get; init; }

    [JsonPropertyName("date_end_real"), JsonConverter(typeof(LooseUnixTimeConverter))]
    public LooseTime ActualEnd { get; init; }
}

internal sealed class DolibarrCustomerDto
{
    [JsonPropertyName("id"), JsonConverter(typeof(LooseStringConverter))]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("code_client")]
    public string? CustomerCode { get; init; }
}

/// <summary>
/// A Dolibarr date: absent ("" or null), a time, or unreadable. Unreadable
/// is kept apart from absent so it is never read as "no date".
/// </summary>
internal readonly record struct LooseTime(DateTimeOffset? Value, bool IsUnreadable)
{
    public static LooseTime Absent => default;
}

/// <summary>A string, or a number written as its digits.</summary>
internal sealed class LooseStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var whole)
                ? whole.ToString(CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Expected a string or a number, not {reader.TokenType}.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

/// <summary>Unix seconds as a number or digits; "" and null are absent.</summary>
internal sealed class LooseUnixTimeConverter : JsonConverter<LooseTime>
{
    public override bool HandleNull => true;

    public override LooseTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long seconds;

        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return LooseTime.Absent;

            case JsonTokenType.Number when reader.TryGetInt64(out seconds):
                break;

            case JsonTokenType.String:
                var text = reader.GetString();

                if (string.IsNullOrWhiteSpace(text))
                {
                    return LooseTime.Absent;
                }

                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seconds))
                {
                    return new LooseTime(null, true);
                }

                break;

            default:
                reader.Skip();
                return new LooseTime(null, true);
        }

        return seconds is < 0 or > 253402300799
            ? new LooseTime(null, true)
            : new LooseTime(DateTimeOffset.FromUnixTimeSeconds(seconds), false);
    }

    public override void Write(Utf8JsonWriter writer, LooseTime value, JsonSerializerOptions options) =>
        throw new NotSupportedException();
}
