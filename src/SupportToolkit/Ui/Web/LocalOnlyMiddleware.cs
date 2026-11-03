namespace SupportToolkit.Ui.Web;

/// <summary>
/// Applies LaunchSecurity to every request, before anything else can answer
/// it, including the Blazor and SignalR endpoints:
///
///  1. Security headers on every response, including refusals.
///  2. The Host header must be the loopback address and port.
///  3. State-changing requests and WebSocket upgrades must come from the
///     UI's own origin.
///  4. A valid launch token (?t=...) at the root is exchanged for the
///     session cookie and redirects to a clean URL, so the token does not
///     stay in the address bar.
///  5. Everything else needs the session cookie.
///
/// A refusal reveals nothing about the application.
/// </summary>
public sealed class LocalOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly LaunchSecurity _security;

    public LocalOnlyMiddleware(
        RequestDelegate next,
        LaunchSecurity security)
    {
        _next = next;
        _security = security;
    }

    public async Task InvokeAsync(
        HttpContext context)
    {
        AddSecurityHeaders(context);

        var request = context.Request;

        if (!_security.IsAllowedHost(request.Host.Value))
        {
            await Refuse(
                context,
                StatusCodes.Status421MisdirectedRequest);

            return;
        }

        var isWebSocket =
            context.WebSockets.IsWebSocketRequest;

        if (LaunchSecurity.RequiresOriginCheck(
                request.Method,
                isWebSocket)
            && !_security.IsAllowedOrigin(
                request.Headers.Origin.ToString()))
        {
            await Refuse(
                context,
                StatusCodes.Status403Forbidden);

            return;
        }

        // The launch token is only honoured at the root, and only to hand
        // out the session cookie.
        if (HttpMethods.IsGet(request.Method)
            && request.Path == "/"
            && request.Query.TryGetValue(
                LaunchSecurity.TokenParameter,
                out var token)
            && _security.IsValidLaunchToken(token.ToString()))
        {
            context.Response.Cookies.Append(
                LaunchSecurity.CookieName,
                _security.SessionValue,
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/",
                    IsEssential = true
                });

            context.Response.Redirect("/");

            return;
        }

        if (!_security.IsValidSession(
                request.Cookies[LaunchSecurity.CookieName]))
        {
            await Refuse(
                context,
                StatusCodes.Status401Unauthorized);

            return;
        }

        await _next(context);
    }

    private static void AddSecurityHeaders(
        HttpContext context)
    {
        var headers = context.Response.Headers;

        // Nothing is loaded from anywhere but this server. The WebSocket
        // is the live connection of the page itself.
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "connect-src 'self' ws: wss:; " +
            "img-src 'self' data:; " +
            "style-src 'self' 'unsafe-inline'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        headers["Cache-Control"] = "no-store";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }

    private static async Task Refuse(
        HttpContext context,
        int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";

        await context.Response.WriteAsync(
            status == StatusCodes.Status401Unauthorized
                ? "Not authorized. Open the link printed by " +
                  "'SupportToolkit serve'."
                : "Refused.");
    }
}
