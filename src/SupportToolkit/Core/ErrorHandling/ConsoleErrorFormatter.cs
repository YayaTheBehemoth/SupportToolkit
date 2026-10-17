
using System.Net;
using System.Text.Json;

namespace SupportToolkit.Core.ErrorHandling;

/// <summary>
/// Identifies the context in which an HTTP failure occurred.
///
/// Generic is intentionally conservative. A module may use
/// multiple external integrations in a single workflow.
/// </summary>
public enum HttpErrorContext
{
    Generic,
    Acronis,
    ComplianceDevProxy
}

/// <summary>
/// Converts expected failures into concise, operator-facing
/// messages without misattributing them to another provider.
/// </summary>
public static class ConsoleErrorFormatter
{
    private const int MaxDiagnosticDepth = 5;

    public static string Format(
        Exception exception,
        HttpErrorContext context = HttpErrorContext.Generic)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            HttpRequestException httpException =>
                context switch
                {
                    HttpErrorContext.Acronis =>
                        FormatAcronisHttpError(httpException),

                    HttpErrorContext.ComplianceDevProxy =>
                        FormatComplianceHttpError(httpException),

                    _ =>
                        FormatGenericHttpError(httpException)
                },

            JsonException =>
                "Received data did not match the expected API contract.",

            TaskCanceledException =>
                "The request timed out or was canceled before completion.",

            InvalidDataException invalidData =>
                invalidData.Message,

            InvalidOperationException invalidOperation =>
                invalidOperation.Message,

            _ =>
                "SupportToolkit encountered an unexpected error."
        };
    }

    /// <summary>
    /// Preserves the existing Acronis-specific error messages.
    /// </summary>
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

    private static string FormatAcronisHttpError(
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
                "Acronis returned a server error " +
                $"(HTTP {(int)exception.StatusCode}).",

            _ =>
                "Acronis request failed " +
                $"(HTTP {(int)exception.StatusCode})."
        };
    }

    /// <summary>
    /// Provides actionable diagnostics for the current
    /// synthetic Compliance development environment.
    ///
    /// An HTTP response does not establish whether
    /// Dev Proxy or the real Graph service generated it.
    /// </summary>
    private static string FormatComplianceHttpError(
        HttpRequestException exception)
    {
        if (exception.StatusCode is null)
        {
            return
                "Compliance request failed before an HTTP response. " +
                "Check Dev Proxy at 127.0.0.1:8000 and its Graph mocks.";
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "Compliance request returned HTTP 401 (Unauthorized). " +
                "Check Dev Proxy interception and Graph mock configuration. " +
                "Live Graph authorization has not been validated.",

            HttpStatusCode.Forbidden =>
                "Compliance request returned HTTP 403 (Forbidden). " +
                "Check Dev Proxy interception and Graph mock configuration. " +
                "Live Graph permissions have not been validated.",

            HttpStatusCode.TooManyRequests =>
                "Compliance request returned HTTP 429. " +
                "Check the Dev Proxy response configuration.",

            _ when (int)exception.StatusCode >= 500 =>
                "Compliance request failed " +
                $"(HTTP {(int)exception.StatusCode}). " +
                "Check Dev Proxy logs and mock responses.",

            _ =>
                "Compliance request failed " +
                $"(HTTP {(int)exception.StatusCode}). " +
                "Check Dev Proxy logs and mock responses."
        };
    }

    /// <summary>
    /// Used when the application cannot safely identify
    /// which external provider generated a failure.
    /// </summary>
    private static string FormatGenericHttpError(
        HttpRequestException exception)
    {
        if (exception.StatusCode is null)
        {
            return
                "HTTP request failed before a response was received. " +
                "Check network connectivity and the active connection.";
        }

        return exception.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "HTTP request returned 401 (Unauthorized). " +
                "Check authentication for the active connection.",

            HttpStatusCode.Forbidden =>
                "HTTP request returned 403 (Forbidden). " +
                "Check permissions for the active connection.",

            HttpStatusCode.TooManyRequests =>
                "HTTP request returned 429 (Too Many Requests). " +
                "Retry later.",

            _ when (int)exception.StatusCode >= 500 =>
                "HTTP request failed with a server error " +
                $"(HTTP {(int)exception.StatusCode}).",

            _ =>
                "HTTP request failed " +
                $"(HTTP {(int)exception.StatusCode})."
        };
    }
}
