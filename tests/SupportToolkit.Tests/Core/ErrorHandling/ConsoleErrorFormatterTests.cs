using System.Net;
using System.Text.Json;
using SupportToolkit.Core.ErrorHandling;

namespace SupportToolkit.Tests.Core.ErrorHandling;

public class ConsoleErrorFormatterTests
{
    [Fact]
    public void Format_Unauthorized_ReturnsAuthenticationMessage()
    {
        var exception = new HttpRequestException(
            "Unauthorized",
            null,
            HttpStatusCode.Unauthorized
        );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "authentication failed",
            message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Format_Forbidden_ReturnsPermissionMessage()
    {
        var exception = new HttpRequestException(
            "Forbidden",
            null,
            HttpStatusCode.Forbidden
        );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "required permissions",
            message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Format_RateLimited_ReturnsRateLimitMessage()
    {
        var exception = new HttpRequestException(
            "Too many requests",
            null,
            HttpStatusCode.TooManyRequests
        );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "rate limit",
            message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void Format_ServerError_ReturnsServerErrorMessage()
    {
        var exception = new HttpRequestException(
            "Server error",
            null,
            HttpStatusCode.InternalServerError
        );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "HTTP 500",
            message
        );
    }

    [Fact]
    public void Format_NetworkFailure_ReturnsConnectivityMessage()
    {
        var exception =
            new HttpRequestException(
                "Connection failed"
            );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "Could not reach Acronis",
            message
        );
    }

    [Fact]
    public void Format_InvalidOperation_PreservesUsefulMessage()
    {
        var exception =
            new InvalidOperationException(
                "Required environment variable " +
                "'ACRONIS_CLIENT_ID' is not configured."
            );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Equal(
            exception.Message,
            message
        );
    }

    [Fact]
    public void Format_JsonException_ReturnsContractMessage()
    {
        var exception =
            new JsonException(
                "Bad JSON"
            );

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "expected API contract",
            message
        );
    }
}