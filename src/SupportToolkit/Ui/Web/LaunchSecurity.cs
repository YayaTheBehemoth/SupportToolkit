using System.Security.Cryptography;
using System.Text;

namespace SupportToolkit.Ui.Web;

/// <summary>
/// The rules that keep the local web UI local and private. Pure logic, no
/// HTTP types, so it is tested directly.
///
/// The server listens on loopback only. On top of that:
///  - A random launch token is printed in the URL the operator opens. It is
///    exchanged once per browser for a random session cookie; nothing else
///    is accepted, so another local web page or process cannot use the UI.
///  - The Host header must be the loopback address and port, which defeats
///    DNS rebinding.
///  - A state-changing request or a WebSocket upgrade must carry an Origin
///    that is the UI itself, which defeats cross-site requests.
/// </summary>
public sealed class LaunchSecurity
{
    public const string CookieName = "SupportToolkitSession";

    public const string TokenParameter = "t";

    private readonly byte[] _tokenBytes;
    private readonly byte[] _sessionBytes;

    private HashSet<string> _allowedHosts = [];
    private HashSet<string> _allowedOrigins = [];

    public LaunchSecurity()
        : this(CreateSecret(), CreateSecret())
    {
    }

    public LaunchSecurity(
        string launchToken,
        string sessionValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launchToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionValue);

        LaunchToken = launchToken;
        SessionValue = sessionValue;

        _tokenBytes = Encoding.UTF8.GetBytes(launchToken);
        _sessionBytes = Encoding.UTF8.GetBytes(sessionValue);
    }

    /// <summary>The secret in the URL the operator opens.</summary>
    public string LaunchToken { get; }

    /// <summary>The value of the session cookie after the token exchange.</summary>
    public string SessionValue { get; }

    /// <summary>
    /// Sets the address the server is actually listening on. Until it is
    /// set, every request is refused.
    /// </summary>
    public void SetListeningPort(
        int port)
    {
        _allowedHosts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"127.0.0.1:{port}",
                $"localhost:{port}"
            };

        _allowedOrigins =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                $"http://127.0.0.1:{port}",
                $"http://localhost:{port}"
            };
    }

    public bool IsAllowedHost(
        string? host) =>
        host is not null && _allowedHosts.Contains(host);

    public bool IsAllowedOrigin(
        string? origin) =>
        origin is not null && _allowedOrigins.Contains(origin);

    public bool IsValidLaunchToken(
        string? token) =>
        Matches(token, _tokenBytes);

    public bool IsValidSession(
        string? cookieValue) =>
        Matches(cookieValue, _sessionBytes);

    /// <summary>
    /// Methods that change state, and WebSocket upgrades, need an Origin
    /// that is the UI itself.
    /// </summary>
    public static bool RequiresOriginCheck(
        string method,
        bool isWebSocketUpgrade) =>
        isWebSocketUpgrade
        || !(HttpMethods.IsGet(method)
             || HttpMethods.IsHead(method)
             || HttpMethods.IsOptions(method));

    private static bool Matches(
        string? candidate,
        byte[] expected)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            expected);
    }

    private static string CreateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
