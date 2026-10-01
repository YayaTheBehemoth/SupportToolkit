using System.Net;
using System.Text.Json;

namespace SupportToolkit.Core.ErrorHandling;

/// <summary>
/// Converts expected application and integration failures into concise,
/// operator-facing error messages suitable for console execution.
/// </summary>
public static class ConsoleErrorFormatter
{
    public static string Format(Exception exception)
    {
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