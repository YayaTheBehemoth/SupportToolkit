using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SupportToolkit.Providers.Zammad.Transport;

/// <summary>
/// Zammad answered a read with something other than success. The message
/// is Zammad's own (error_human, else error), on one line and shortened;
/// it never contains the token.
/// </summary>
public sealed class ZammadApiException : Exception
{
    public ZammadApiException(
        HttpStatusCode statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>Zammad's error text, for messages.</summary>
public static partial class ZammadErrorText
{
    private const int MaximumLength = 300;

    /// <summary>
    /// Zammad's own message (error_human, else error), on one line and at
    /// most 300 characters; the raw body when it is not Zammad's error JSON.
    /// </summary>
    public static string From(string body)
    {
        string? text = null;

        try
        {
            using var json = JsonDocument.Parse(body);

            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                text =
                    Read(json.RootElement, "error_human")
                    ?? Read(json.RootElement, "error");
            }
        }
        catch (JsonException)
        {
        }

        var line = Whitespace().Replace(text ?? body, " ").Trim();

        if (line.Length == 0)
        {
            return "(no details)";
        }

        return line.Length <= MaximumLength
            ? line
            : line[..MaximumLength] + "…";
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
