using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Web;

namespace SupportToolkit.Providers.Zendesk.Deletion.Auth;

/// <summary>
/// Opens the authorization page for the operator. Behind an interface so
/// tests never launch a browser.
/// </summary>
public interface IZendeskBrowserLauncher
{
    void Open(Uri url);
}

/// <summary>
/// Receives the OAuth redirect on a loopback address.
/// </summary>
public interface IZendeskLoopbackReceiver : IDisposable
{
    /// <summary>
    /// The redirect URI to send, for example http://localhost:51234.
    /// </summary>
    string RedirectUri { get; }

    /// <summary>
    /// Waits for the browser to be redirected back and returns the
    /// recognised query parameters (code, state, error,
    /// error_description). Nothing else is read.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> WaitForRedirectAsync(
        CancellationToken cancellationToken);
}

public sealed class SystemBrowserLauncher : IZendeskBrowserLauncher
{
    public void Open(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (url.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Only an HTTPS sign-in page is opened.");
        }

        Process.Start(
            new ProcessStartInfo(url.AbsoluteUri)
            {
                UseShellExecute = true
            });
    }
}

/// <summary>
/// Listens for the redirect on a free loopback port.
///
/// The redirect is registered as http://localhost; Zendesk ignores the
/// port, so any free port is used and sent as http://localhost:{port}.
/// A localhost prefix is served on both IPv4 and IPv6 loopback, whichever
/// the browser resolves "localhost" to. It listens only on loopback and
/// only until one redirect has been received.
/// </summary>
public sealed class ZendeskLoopbackReceiver : IZendeskLoopbackReceiver
{
    private const string Allowed = "code state error error_description";

    private readonly HttpListener _listener;

    private ZendeskLoopbackReceiver(
        HttpListener listener,
        int port)
    {
        _listener = listener;
        RedirectUri = $"http://localhost:{port}";
    }

    public string RedirectUri { get; }

    public static ZendeskLoopbackReceiver Start()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var port = FindFreePort();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");

            try
            {
                listener.Start();

                return new ZendeskLoopbackReceiver(listener, port);
            }
            catch (HttpListenerException)
            {
                // The port was taken between probing and binding.
                listener.Close();
            }
        }

        throw new InvalidOperationException(
            "No free loopback port was available for the Zendesk " +
            "sign-in redirect.");
    }

    public async Task<IReadOnlyDictionary<string, string>>
        WaitForRedirectAsync(
            CancellationToken cancellationToken)
    {
        while (true)
        {
            var context =
                await _listener.GetContextAsync()
                    .WaitAsync(cancellationToken);

            var query =
                HttpUtility.ParseQueryString(
                    context.Request.Url?.Query ?? string.Empty);

            var found = new Dictionary<string, string>(
                StringComparer.Ordinal);

            foreach (var key in Allowed.Split(' '))
            {
                var value = query[key];

                if (!string.IsNullOrEmpty(value))
                {
                    found[key] = value;
                }
            }

            var isRedirect =
                context.Request.Url?.AbsolutePath == "/"
                && (found.ContainsKey("code")
                    || found.ContainsKey("error"));

            // Ignore stray requests such as a favicon fetch.
            if (!isRedirect)
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }

            await Respond(
                context,
                found.ContainsKey("error")
                    ? "Sign-in was not completed. You can close this " +
                      "window and return to SupportToolkit."
                    : "Sign-in complete. You can close this window and " +
                      "return to SupportToolkit.");

            return found;
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private static async Task Respond(
        HttpListenerContext context,
        string message)
    {
        // A fixed page: nothing from the request is echoed back.
        var body =
            System.Text.Encoding.UTF8.GetBytes(
                "<!doctype html><meta charset=\"utf-8\">" +
                "<title>SupportToolkit</title><p>" + message + "</p>");

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;

        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }
}
