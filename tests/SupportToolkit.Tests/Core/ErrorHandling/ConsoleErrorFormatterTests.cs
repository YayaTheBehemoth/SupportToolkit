
using System.Net;
using System.Text.Json;
using SupportToolkit.Core.ErrorHandling;

namespace SupportToolkit.Tests.Core.ErrorHandling;

public class ConsoleErrorFormatterTests
{
    private const string SensitiveMarker =
        "synthetic-secret-do-not-print";

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication failed")]
    [InlineData(HttpStatusCode.Forbidden, "required permissions")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    public void Format_HttpError_ReturnsOperatorMessage(
        HttpStatusCode status,
        string expectedMessage)
    {
        var exception = new HttpRequestException(
            "Synthetic provider error",
            null,
            status);

        var message = ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            expectedMessage,
            message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_NetworkFailure_ReturnsConnectivityMessage()
    {
        var exception = new HttpRequestException(
            "Connection failed");

        var message = ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "Could not reach Acronis",
            message);
    }

    [Fact]
    public void Format_InvalidOperation_PreservesOperatorMessage()
    {
        const string expected =
            "Required environment variable 'ACRONIS_CLIENT_ID' " +
            "is not configured.";

        var exception = new InvalidOperationException(expected);

        Assert.Equal(
            expected,
            ConsoleErrorFormatter.Format(exception));
    }

    [Fact]
    public void Format_JsonException_ReturnsContractMessage()
    {
        var exception = new JsonException("Bad JSON");

        var message = ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "expected API contract",
            message);
    }

    [Fact]
    public void FormatDiagnostic_RedactsExceptionMessage()
    {
        var exception = new InvalidOperationException(
            $"Vendor failed with token={SensitiveMarker}");

        var diagnostic =
            ConsoleErrorFormatter.FormatDiagnostic(exception);

        Assert.Contains(
            "Exception chain: InvalidOperationException",
            diagnostic);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostic);
    }

    [Fact]
    public void FormatDiagnostic_ReportsHttpStatusWithoutRequestUrl()
    {
        var exception = new HttpRequestException(
            $"GET https://fixture.invalid/api?token={SensitiveMarker}",
            null,
            HttpStatusCode.Forbidden);

        var diagnostic =
            ConsoleErrorFormatter.FormatDiagnostic(exception);

        Assert.Contains(
            "HttpRequestException (HTTP 403)",
            diagnostic);

        Assert.DoesNotContain(
            "https://fixture.invalid",
            diagnostic);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostic);
    }

    [Fact]
    public void FormatDiagnostic_ReportsInnerTypesWithoutMessages()
    {
        var inner = new HttpRequestException(
            $"Remote response contained {SensitiveMarker}",
            null,
            HttpStatusCode.ServiceUnavailable);

        var outer = new InvalidOperationException(
            "Zendesk submission failed.",
            inner);

        var diagnostic =
            ConsoleErrorFormatter.FormatDiagnostic(outer);

        Assert.Contains(
            "InvalidOperationException",
            diagnostic);

        Assert.Contains(
            "HttpRequestException (HTTP 503)",
            diagnostic);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostic);

        Assert.DoesNotContain(
            "Zendesk submission failed.",
            diagnostic);
    }

    [Fact]
    public void FormatDiagnostic_LimitsNestedExceptionDepth()
    {
        Exception exception =
            new InvalidOperationException(SensitiveMarker);

        for (var index = 0; index < 10; index++)
        {
            exception = new InvalidOperationException(
                SensitiveMarker,
                exception);
        }

        var diagnostic =
            ConsoleErrorFormatter.FormatDiagnostic(exception);

        Assert.Contains(
            "additional inner exceptions omitted",
            diagnostic);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostic);
    }
}
