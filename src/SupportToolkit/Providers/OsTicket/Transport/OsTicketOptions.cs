using System.Net;

namespace SupportToolkit.Providers.OsTicket.Transport;

/// <summary>
/// Validated settings for one osTicket helpdesk.
///
/// BaseUri is the helpdesk root, always ending in '/', so api/ and scp/
/// resolve below it even when osTicket is installed in a folder. HTTPS is
/// required because the API key travels in a header; plain HTTP is
/// accepted only for a loopback host, such as a local lab.
/// </summary>
public sealed class OsTicketOptions
{
    private OsTicketOptions(
        Uri baseUri,
        string apiKey)
    {
        BaseUri = baseUri;
        ApiKey = apiKey;
    }

    public Uri BaseUri { get; }

    public string ApiKey { get; }

    public static OsTicketOptions Create(
        string baseUrl,
        string apiKey)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The osTicket base URL must be an absolute URL, for " +
                "example https://helpdesk.example.com/.");
        }

        var isLoopback =
            uri.IsLoopback
            || (IPAddress.TryParse(uri.Host, out var address)
                && IPAddress.IsLoopback(address));

        if (uri.Scheme != Uri.UriSchemeHttps
            && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            throw new InvalidOperationException(
                "The osTicket base URL must use HTTPS. Plain HTTP is " +
                "accepted only for a loopback address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "The osTicket base URL must not contain credentials, a " +
                "query or a fragment.");
        }

        if (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Any(character =>
                char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException(
                "The osTicket API key is empty or contains spaces or " +
                "control characters.");
        }

        var root =
            uri.AbsolutePath.EndsWith('/')
                ? uri
                : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");

        return new OsTicketOptions(root, apiKey.Trim());
    }
}
