using System.Net;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// Business Central returned a non-success HTTP status.
///
/// Messages are operator-safe: no response body, URL, company,
/// tenant or customer identifier is ever included.
/// </summary>
public sealed class BusinessCentralApiException
    : InvalidOperationException
{
    public HttpStatusCode StatusCode { get; }

    public BusinessCentralApiException(
        HttpStatusCode statusCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// Business Central returned data that violates the expected
/// contract, or pagination could not be completed safely.
/// Never treated as an empty result.
/// </summary>
public sealed class BusinessCentralContractException
    : InvalidOperationException
{
    public BusinessCentralContractException(
        string message,
        Exception? innerException = null)
        : base(
            message,
            innerException)
    {
    }
}
