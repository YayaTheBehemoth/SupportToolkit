
using System.Net;
using SupportToolkit.Core.ErrorHandling;

namespace SupportToolkit.Tests.Core.ErrorHandling;

public class ConsoleErrorDiagnosticsTests
{
    private const string SensitiveMarker =
        "synthetic-secret-do-not-print";

    [Fact]
    public void Diagnostic_DoesNotIncludeRawExceptionMessage()
    {
        var exception =
            new InvalidOperationException(
                $"Vendor failed with token={SensitiveMarker}");

        var diagnostic =
            ConsoleErrorFormatter.FormatDiagnostic(exception);

        Assert.Contains(
            "InvalidOperationException",
            diagnostic);

        Assert.DoesNotContain(
            SensitiveMarker,
            diagnostic);
    }

    [Fact]
    public void Diagnostic_ReportsHttpStatusWithoutRequestUrl()
    {
        var exception =
            new HttpRequestException(
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
    public void Diagnostic_ReportsInnerExceptionTypesWithoutMessages()
    {
        var inner =
            new HttpRequestException(
                $"Remote response contained {SensitiveMarker}",
                null,
                HttpStatusCode.ServiceUnavailable);

        var outer =
            new InvalidOperationException(
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
    public void Diagnostic_LimitsNestedExceptionDepth()
    {
        Exception exception =
            new InvalidOperationException(SensitiveMarker);

        for (var index = 0; index < 10; index++)
        {
            exception =
                new InvalidOperationException(
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

    [Fact]
    public void OperatorMessage_RemainsUnchanged()
    {
        var exception =
            new InvalidOperationException(
                "Zendesk ticket creation was not confirmed. " +
                "Check the destination before retrying.");

        var message =
            ConsoleErrorFormatter.Format(exception);

        Assert.Contains(
            "before retrying",
            message);

        Assert.DoesNotContain(
            "Exception chain:",
            message);
    }
}
