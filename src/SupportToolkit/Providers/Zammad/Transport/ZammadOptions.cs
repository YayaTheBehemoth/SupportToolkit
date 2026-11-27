using System.Net;

namespace SupportToolkit.Providers.Zammad.Transport;

/// <summary>
/// Validated settings for one Zammad instance.
///
/// BaseUri is the Zammad root, always ending in '/', so api/ resolves below
/// it even when Zammad is installed in a folder. HTTPS is required because
/// the token travels in a header; plain HTTP is accepted only for a
/// loopback host, such as a local lab.
/// </summary>
public sealed class ZammadOptions
{
    private ZammadOptions(
        Uri baseUri,
        string apiToken)
    {
        BaseUri = baseUri;
        ApiToken = apiToken;
    }

    public Uri BaseUri { get; }

    public string ApiToken { get; }

    public static ZammadOptions Create(
        string baseUrl,
        string apiToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "The Zammad base URL must be an absolute URL, for " +
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
                "The Zammad base URL must use HTTPS. Plain HTTP is " +
                "accepted only for a loopback address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                "The Zammad base URL must not contain credentials, a " +
                "query or a fragment.");
        }

        if (string.IsNullOrWhiteSpace(apiToken)
            || apiToken.Any(character =>
                char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException(
                "The Zammad API token is empty or contains spaces or " +
                "control characters.");
        }

        var root =
            uri.AbsolutePath.EndsWith('/')
                ? uri
                : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");

        return new ZammadOptions(root, apiToken.Trim());
    }
}
