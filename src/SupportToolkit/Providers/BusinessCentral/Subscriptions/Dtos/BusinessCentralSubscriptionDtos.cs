using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

/// <summary>
/// Reads an enum-like column whose JSON representation is not yet
/// verified. Strings, booleans and numbers are preserved as their
/// textual form so the evidence is never lost; objects and arrays
/// fail the contract.
/// </summary>
public sealed class BusinessCentralScalarTextConverter
    : JsonConverter<string>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String =>
                reader.GetString(),

            JsonTokenType.True =>
                "true",

            JsonTokenType.False =>
                "false",

            JsonTokenType.Number =>
                Encoding.UTF8.GetString(
                    reader.HasValueSequence
                        ? reader.ValueSequence.ToArray()
                        : reader.ValueSpan.ToArray()
                ),

            _ =>
                throw new JsonException(
                    "Unsupported representation for a scalar column."
                )
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        string value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}

/*
 * Contract basis: Power Query Table.Schema exports of the custom
 * subscription header and line API pages.
 *
 * Only fields needed for entitlement evidence are modeled. The pages
 * also expose pricing, contact and e-mail fields; these are
 * deliberately absent here and excluded from every request through
 * $select (see BusinessCentralSubscriptionFields).
 *
 * Power Query reports nearly every column as nullable regardless of
 * the source, so nullability here is conservative and not evidence
 * that BC returns nulls.
 *
 * Enum-like columns (exported as Any.Type) are read as strings. An
 * unexpected non-string representation fails the contract rather
 * than being coerced.
 */

public sealed class BusinessCentralSubscriptionHeaderDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Subscription number; join key for lines.</summary>
    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(BusinessCentralScalarTextConverter))]
    public string? Type { get; init; }

    [JsonPropertyName("blocked")]
    [JsonConverter(typeof(BusinessCentralScalarTextConverter))]
    public string? Blocked { get; init; }

    [JsonPropertyName("sellToCustomerNumber")]
    public string? SellToCustomerNumber { get; init; }

    [JsonPropertyName("billToCustomerNumber")]
    public string? BillToCustomerNumber { get; init; }

    /// <summary>
    /// Raw date text. Business Central may represent an empty date
    /// with a sentinel value, so interpretation is explicit and
    /// happens during mapping, not during deserialization.
    /// </summary>
    [JsonPropertyName("startDate")]
    public string? StartDate { get; init; }

    [JsonPropertyName("endDate")]
    public string? EndDate { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; init; }
}

public sealed class BusinessCentralSubscriptionLineDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Header number this line belongs to.</summary>
    [JsonPropertyName("subscriptionNumber")]
    public string? SubscriptionNumber { get; init; }

    [JsonPropertyName("lineNumber")]
    public int? LineNumber { get; init; }

    [JsonPropertyName("type")]
    [JsonConverter(typeof(BusinessCentralScalarTextConverter))]
    public string? Type { get; init; }

    /// <summary>Item (or other line type) number.</summary>
    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; init; }

    [JsonPropertyName("omitLine")]
    public bool? OmitLine { get; init; }

    [JsonPropertyName("headerCategory")]
    public string? HeaderCategory { get; init; }

    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    [JsonPropertyName("company")]
    public string? Company { get; init; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; init; }
}

/// <summary>
/// Customer name carried on a subscription header row.
///
/// Confirmed against the page's $metadata: sellToCustomerName is a
/// header property next to sellToCustomerNumber. It is read through
/// its own request so the established header contract is unaffected
/// if the column is ever renamed: only this read would fail, never
/// the entitlement read.
/// </summary>
public sealed class BusinessCentralHeaderCustomerNameDto
{
    [JsonPropertyName("number")]
    public string? Number { get; init; }

    [JsonPropertyName("sellToCustomerNumber")]
    public string? SellToCustomerNumber { get; init; }

    [JsonPropertyName("sellToCustomerName")]
    public string? SellToCustomerName { get; init; }
}

/// <summary>
/// Field lists sent as $select so data outside the DTOs is never
/// requested from Business Central.
/// </summary>
public static class BusinessCentralSubscriptionFields
{
    public const string HeaderSelect =
        "id,number,type,blocked,sellToCustomerNumber," +
        "billToCustomerNumber,startDate,endDate,status," +
        "lastModifiedDateTime";

    public const string HeaderCustomerNameSelect =
        "number,sellToCustomerNumber,sellToCustomerName";

    public const string LineSelect =
        "id,subscriptionNumber,lineNumber,type,number,quantity," +
        "omitLine,headerCategory,environment,company," +
        "lastModifiedDateTime";
}
