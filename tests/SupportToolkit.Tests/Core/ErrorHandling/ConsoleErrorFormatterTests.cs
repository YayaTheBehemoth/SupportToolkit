
using System.Net;
using System.Text.Json;
using SupportToolkit.Core.ErrorHandling;

namespace SupportToolkit.Tests.Core.ErrorHandling;

public class ConsoleErrorFormatterTests
{
    private const string SensitiveMarker = "synthetic-secret-do-not-print";

    [Fact]
    public void Format_AcronisUnauthorized_PreservesAuthenticationMessage()
    {
        var message = FormatHttp(
            HttpStatusCode.Unauthorized,
            HttpErrorContext.Acronis
        );

        Assert.Contains(
            "Acronis authentication failed",
            message
        );

        Assert.Contains(
            "API client ID and secret",
            message
        );
    }

    [Fact]
    public void Format_AcronisForbidden_PreservesPermissionMessage()
    {
        var message = FormatHttp(
            HttpStatusCode.Forbidden,
            HttpErrorContext.Acronis
        );

        Assert.Contains(
            "Acronis rejected the request",
            message
        );

        Assert.Contains(
            "required permissions",
            message
        );
    }

    [Fact]
    public void Format_AcronisRateLimited_PreservesRateLimitMessage()
    {
        var message = FormatHttp(
            HttpStatusCode.TooManyRequests,
            HttpErrorContext.Acronis
        );

        Assert.Contains(
            "Acronis rate limit reached",
            message
        );
    }

    [Fact]
    public void Format_AcronisServerError_PreservesStatusCode()
    {
        var message = FormatHttp(
            HttpStatusCode.InternalServerError,
            HttpErrorContext.Acronis
        );

        Assert.Contains(
            "Acronis returned a server error",
            message
        );

        Assert.Contains("HTTP 500", message);
    }

    [Fact]
    public void Format_AcronisNetworkFailure_PreservesMessage()
    {
        var message = ConsoleErrorFormatter.Format(
            new HttpRequestException("Connection failed"),
            HttpErrorContext.Acronis
        );

        Assert.Contains(
            "Could not reach Acronis",
            message
        );
    }

    [Fact]
    public void Format_ComplianceNetworkFailure_PointsToDevProxy()
    {
        var message = ConsoleErrorFormatter.Format(
            new HttpRequestException("Connection failed"),
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("Compliance request", message);
        Assert.Contains("127.0.0.1:8000", message);
        Assert.Contains("Graph mocks", message);

        Assert.DoesNotContain("Acronis", message);
    }

    [Fact]
    public void Format_ComplianceUnauthorized_DoesNotBlameAcronis()
    {
        var message = FormatHttp(
            HttpStatusCode.Unauthorized,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("HTTP 401", message);
        Assert.Contains("Dev Proxy", message);
        Assert.Contains("Graph mock", message);

        Assert.DoesNotContain("Acronis", message);
        Assert.DoesNotContain("client ID and secret", message);
    }

    [Fact]
    public void Format_ComplianceForbidden_DoesNotAssertLiveGraphPermissions()
    {
        var message = FormatHttp(
            HttpStatusCode.Forbidden,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("HTTP 403", message);
        Assert.Contains("not been validated", message);

        Assert.DoesNotContain("Acronis", message);
    }

    [Fact]
    public void Format_ComplianceRateLimited_PreservesStatusCode()
    {
        var message = FormatHttp(
            HttpStatusCode.TooManyRequests,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("HTTP 429", message);
        Assert.Contains("Dev Proxy", message);
    }

    [Fact]
    public void Format_ComplianceServerError_PreservesStatusCode()
    {
        var message = FormatHttp(
            HttpStatusCode.InternalServerError,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("HTTP 500", message);
        Assert.Contains("Dev Proxy logs", message);
    }

    [Fact]
    public void Format_ComplianceOtherStatus_PreservesStatusCode()
    {
        var message = FormatHttp(
            HttpStatusCode.NotFound,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Contains("HTTP 404", message);
        Assert.DoesNotContain("Acronis", message);
    }

    [Fact]
    public void Format_GenericNetworkFailure_DoesNotNameProvider()
    {
        var message = ConsoleErrorFormatter.Format(
            new HttpRequestException("Connection failed")
        );

        Assert.Contains(
            "HTTP request failed",
            message
        );

        Assert.DoesNotContain("Acronis", message);
        Assert.DoesNotContain("Zendesk", message);
    }

    [Fact]
    public void Format_GenericUnauthorized_DoesNotNameProvider()
    {
        var message = FormatHttp(
            HttpStatusCode.Unauthorized,
            HttpErrorContext.Generic
        );

        Assert.Contains("401", message);

        Assert.DoesNotContain("Acronis", message);
        Assert.DoesNotContain("Zendesk", message);
    }

    [Fact]
    public void Format_GenericForbidden_ProvidesPermissionMessage()
    {
        var message = FormatHttp(
            HttpStatusCode.Forbidden,
            HttpErrorContext.Generic
        );

        Assert.Contains("403", message);
        Assert.Contains("permissions", message);
    }

    [Fact]
    public void Format_GenericServerError_PreservesStatusCode()
    {
        var message = FormatHttp(
            HttpStatusCode.InternalServerError,
            HttpErrorContext.Generic
        );

        Assert.Contains("HTTP 500", message);
        Assert.DoesNotContain("Acronis", message);
    }

    [Fact]
    public void Format_InvalidData_PreservesCollectionError()
    {
        var exception = new InvalidDataException(
            "Duplicate managed-device ID returned. " +
            "Compliance inventory review aborted."
        );

        var message = ConsoleErrorFormatter.Format(
            exception,
            HttpErrorContext.ComplianceDevProxy
        );

        Assert.Equal(exception.Message, message);
    }

    [Fact]
    public void Format_InvalidOperation_PreservesUsefulMessage()
    {
        var exception = new InvalidOperationException(
            "Required environment variable " +
            "'ACRONIS_CLIENT_ID' is not configured."
        );

        var message = ConsoleErrorFormatter.Format(
            exception
        );

        Assert.Equal(exception.Message, message);
    }

    [Fact]
    public void Format_JsonException_ReturnsContractMessage()
    {
        var message = ConsoleErrorFormatter.Format(
            new JsonException("Bad JSON")
        );

        Assert.Contains(
            "expected API contract",
            message
        );
    }

    [Fact]
    public void Format_TaskCanceled_ReturnsTimeoutMessage()
    {
        var message = ConsoleErrorFormatter.Format(
            new TaskCanceledException("Request canceled")
        );

        Assert.Contains(
            "timed out or was canceled",
            message
        );
    }

    [Fact]
    public void Format_UnexpectedException_DoesNotExposeDetails()
    {
        var message = ConsoleErrorFormatter.Format(
            new Exception("Sensitive implementation detail")
        );

        Assert.Contains(
            "unexpected error",
            message
        );

        Assert.DoesNotContain(
            "Sensitive implementation detail",
            message
        );
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
    private static string FormatHttp(
        HttpStatusCode status,
        HttpErrorContext context)
    {
        var exception = new HttpRequestException(
            "Synthetic HTTP failure",
            null,
            status
        );

        return ConsoleErrorFormatter.Format(
            exception,
            context
        );
    }
}
