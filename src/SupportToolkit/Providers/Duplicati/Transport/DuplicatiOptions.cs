using System.Net;

namespace SupportToolkit.Providers.Duplicati.Transport;

/// <summary>
/// Validates a Duplicati server address. HTTPS is required because the
/// password and the token travel over it; plain HTTP is accepted only for
/// a loopback host, such as a local lab.
/// </summary>
public static class DuplicatiOptions
{
    public static Uri ParseServerUrl(string url, string serverAlias)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Duplicati server '{serverAlias}' needs an absolute url, " +
                "for example https://backup.example.com/.");
        }

        var isLoopback =
            uri.IsLoopback
            || (IPAddress.TryParse(uri.Host, out var address)
                && IPAddress.IsLoopback(address));

        if (uri.Scheme != Uri.UriSchemeHttps
            && !(uri.Scheme == Uri.UriSchemeHttp && isLoopback))
        {
            throw new InvalidOperationException(
                $"Duplicati server '{serverAlias}' must use HTTPS. Plain " +
                "HTTP is accepted only for a loopback address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"Duplicati server '{serverAlias}' url must not contain " +
                "credentials, a query or a fragment.");
        }

        return uri.AbsolutePath.EndsWith('/')
            ? uri
            : new Uri(uri.GetLeftPart(UriPartial.Path) + "/");
    }
}
