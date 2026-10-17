
using System.Net;
using System.Text.Json;

namespace SupportToolkit.Core.ErrorHandling;

/// <summary>
/// Converts application and integration failures into concise
/// operator-facing errors and bounded debug diagnostics.
///
/// Debug diagnostics deliberately exclude exception messages,
/// stack traces, request URLs, and response bodies.
/// </summary>
public static class ConsoleErrorFormatter
{
    private const int MaxDiagnosticDepth = 5;

    public static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            HttpRequestException httpException =>
                FormatHttpError(httpException),

            JsonException =>
                "Received data did not match the expected API contract.",

            TaskCanceledException =>
                "The request timed out before it could complete.",

            InvalidOperationException invalidOperation =>
                invalidOperation.Message,

            _ =>
                "SupportToolkit encountered an unexpected error."
        };
    }

    /// <summary>
    /// Returns an additional debug diagnostic using only
    /// exception types and structured HTTP status codes.
    ///
    /// This method must not include Exception.Message,
    /// Exception.ToString(), StackTrace, or arbitrary exception Data.
    /// </summary>
    public static string FormatDiagnostic(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var descriptions =
            new List<string>();

        Exception? current =
            exception;

        for (var depth = 0;
             depth < MaxDiagnosticDepth && current is not null;
             depth++)
        {
            var description =
                current.GetType().Name;

            if (current is HttpRequestException httpException
                && httpException.StatusCode is HttpStatusCode statusCode)
            {
                description +=
                    $" (HTTP {(int)statusCode})";
            }

            descriptions.Add(description);

            current =
                current.InnerException;
        }

        if (current is not null)
        {
            descriptions.Add(
                "additional inner exceptions omitted");
        }

        return
            "Exception chain: " +
            string.Join(" -> ", descriptions);
    }

    private static string FormatHttpError(
        HttpRequestException exception)
    {
        if (exception.StatusCode is null)
        {
            return
                "Could not reach Acronis. Check network connectivity " +
                "and the configured data-center URL.";
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Acronis authentication failed. " +
                "Check the API client ID and secret.",

            HttpStatusCode.Forbidden =>
                "Acronis rejected the request because the API client " +
                "does not have the required permissions.",

            HttpStatusCode.TooManyRequests =>
                "Acronis rate limit reached. Try again later.",

            _ when (int)exception.StatusCode >= 500 =>
                $"Acronis returned a server error " +
                $"(HTTP {(int)exception.StatusCode}).",

            _ =>
                $"Acronis request failed " +
                $"(HTTP {(int)exception.StatusCode})."
        };
    }
}
