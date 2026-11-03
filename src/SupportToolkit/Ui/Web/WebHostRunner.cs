using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SupportToolkit.Ui.Web.Components;

namespace SupportToolkit.Ui.Web;

/// <param name="Port">0 picks a free port.</param>
public sealed record WebServeOptions(
    int Port,
    bool OpenBrowser);

/// <summary>
/// A started web UI. Dispose it to stop the server.
/// </summary>
public sealed class RunningWebHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    internal RunningWebHost(
        WebApplication app,
        LaunchSecurity security,
        int port)
    {
        _app = app;
        Security = security;
        Port = port;
    }

    /// <summary>
    /// The secrets of this run. They never leave the process except in the
    /// link printed for the operator.
    /// </summary>
    public LaunchSecurity Security { get; }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    /// <summary>The link the operator opens; it carries the launch token.</summary>
    public string LaunchUrl =>
        $"{BaseUrl}/?{LaunchSecurity.TokenParameter}={Security.LaunchToken}";

    public Task WaitForShutdownAsync(
        CancellationToken cancellationToken = default) =>
        _app.WaitForShutdownAsync(cancellationToken);

    public Task StopAsync(
        CancellationToken cancellationToken = default) =>
        _app.StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }
}

/// <summary>
/// Hosts the local web UI. The CLI builds this host only for `serve`, so
/// every other command, and `--help`, never touches the web stack.
///
/// The server listens on 127.0.0.1 only, and LaunchSecurity guards every
/// request. Nothing is served from outside the application: the page's
/// stylesheet is embedded, and Blazor's script is a framework asset
/// published beside the executable.
/// </summary>
public static class WebHostRunner
{
    /// <summary>
    /// Starts the server and returns once it is listening.
    /// </summary>
    public static async Task<RunningWebHost> StartAsync(
        WebServeOptions options,
        WebAppState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(state);

        var security = new LaunchSecurity();

        var builder =
            WebApplication.CreateBuilder(
                new WebApplicationOptions
                {
                    Args = [],
                    EnvironmentName = Environments.Production,
                    ContentRootPath = AppContext.BaseDirectory,

                    // Static web assets are found by application name. In
                    // the program this is the entry assembly already; a
                    // test host would otherwise look for its own.
                    ApplicationName =
                        typeof(WebHostRunner).Assembly.GetName().Name
                });

        // Request URLs carry the launch token, so request logging stays
        // off. Warnings and errors still reach the console.
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");

        builder.WebHost.ConfigureKestrel(
            kestrel => kestrel.AddServerHeader = false);

        // Lets a run from the build folder find the framework's script as
        // a published app does. Harmless when there is no such manifest.
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddSingleton(security);
        builder.Services.AddSingleton(state);

        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents(
                server => server.DetailedErrors = false);

        var app = builder.Build();

        app.UseMiddleware<LocalOnlyMiddleware>(security);

        app.UseAntiforgery();

        var stylesheet = ReadEmbedded("app.css");

        app.MapGet(
            "/app.css",
            () => Results.Text(stylesheet, "text/css; charset=utf-8"));

        // Blazor's own script is a framework static asset, listed in the
        // manifest published beside this assembly.
        app.MapStaticAssets(
            Path.Combine(
                AppContext.BaseDirectory,
                $"{typeof(WebHostRunner).Assembly.GetName().Name}" +
                ".staticwebassets.endpoints.json"));

        // The page may not be framed by anything, the same as the policy
        // LocalOnlyMiddleware sets (Blazor would otherwise add its own,
        // looser 'self' policy as a second header).
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode(
                render =>
                    render.ContentSecurityFrameAncestorsPolicy = "'none'");

        await app.StartAsync(cancellationToken);

        var address =
            app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses
                .First();

        var port = new Uri(address).Port;

        // Until the port is known every request is refused.
        security.SetListeningPort(port);

        return new RunningWebHost(app, security, port);
    }

    /// <summary>
    /// Starts the server, tells the operator where it is, and runs until
    /// it is stopped.
    /// </summary>
    public static async Task<int> RunAsync(
        WebServeOptions options,
        WebAppState state,
        CancellationToken cancellationToken = default)
    {
        await using var host =
            await StartAsync(options, state, cancellationToken);

        Console.WriteLine("SupportToolkit web UI");
        Console.WriteLine("---------------------");
        Console.WriteLine(
            $"Listening on {host.BaseUrl} (this machine only).");
        Console.WriteLine();
        Console.WriteLine("Open this link in your browser:");
        Console.WriteLine($"  {host.LaunchUrl}");
        Console.WriteLine();
        Console.WriteLine(
            "The link works with this run only. Press Ctrl+C or use " +
            "Exit in the page to stop.");

        if (options.OpenBrowser)
        {
            TryOpenBrowser(host.LaunchUrl);
        }

        await host.WaitForShutdownAsync(cancellationToken);

        return 0;
    }

    private static string ReadEmbedded(
        string name)
    {
        using var stream =
            Assembly.GetExecutingAssembly()
                .GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{name}' is missing.");

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static void TryOpenBrowser(
        string url)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                  or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine(
                "The browser could not be opened. Open the link above yourself.");
        }
    }
}
