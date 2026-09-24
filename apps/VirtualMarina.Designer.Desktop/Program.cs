using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace VirtualMarina.Designer.Desktop;

/// <summary>
/// Runs the Blazor designer as a windowed app: it serves the WebAssembly build on a local port and opens a Chromium
/// browser in application mode — a chromeless window with no tabs or address bar — pointed at it. Because it is the
/// ordinary browser engine loading the ordinary served app, WebGL and the synchronous JS interop the renderer relies on
/// work exactly as they do in a tab; only the window has no browser furniture.
/// </summary>
/// <remarks>
/// <para>
/// The launcher stops when the window closes, however the window was opened. The page keeps a Server-Sent Events
/// stream open to <c>/launcher/events</c> for as long as it lives and says goodbye on <c>/launcher/bye</c> as it goes;
/// a few seconds after the last page has gone (long enough for a reload) the launcher shuts down, and if no page has
/// connected a minute after starting, it gives up. That works for a Flatpak or snap browser, a window handed to a
/// browser already running and the default browser alike, none of which can be waited on. Where the window's own
/// process could be started, its exit stops the launcher too.
/// </para>
/// <para>
/// The page is opened with a random token in its address, which the launcher's own endpoints require; the page only
/// looks for the launcher when it has one, so the same app served by any static host never tries.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>Tells the page this server is the launcher (the heartbeat's answer), rather than any static host.</summary>
    private const string LauncherName = "VirtualMarina.Designer.Launcher";

    /// <summary>
    /// A window process that ends this soon after starting, before any page connected, handed its window to a browser
    /// already running on the same profile; its exit says nothing about the window.
    /// </summary>
    private static readonly TimeSpan HandOffTime = TimeSpan.FromSeconds(10);

    /// <summary>How often an idle event stream is written to, so a connection that died quietly is noticed.</summary>
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(10);

    private static async Task<int> Main(string[] args)
    {
        var noBrowser = args.Contains("--no-browser", StringComparer.OrdinalIgnoreCase);
        var hostArgs = args.Where(arg => !string.Equals(arg, "--no-browser", StringComparison.OrdinalIgnoreCase)).ToArray();

        // The content root is where the launcher is, not wherever it was started from, so a published copy finds its
        // wwwroot from any working directory.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = hostArgs, ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();     // a launcher, not a server: keep the console quiet
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));   // any free loopback port
        builder.WebHost.UseStaticWebAssets();  // a build (not a publish) serves the client's files from where they were built

        var liveness = new LauncherLiveness();
        var token = RandomNumberGenerator.GetHexString(32, lowercase: true);

        var app = builder.Build();
        MapLauncherEndpoints(app, liveness, token);
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var url = $"{address}/?launcher={token}";
        Console.WriteLine($"VirtualMarina Designer is running at {url}");

        var window = noBrowser ? new BrowserWindow("no browser (--no-browser)", null, Opened: false) : BrowserLauncher.Open(url);
        try
        {
            await RunUntilClosedAsync(app, window, liveness);
        }
        finally
        {
            window.Process?.Dispose();
        }

        return 0;
    }

    /// <summary>Serves until the window is gone, Ctrl+C is pressed, or no window ever connects; then stops.</summary>
    private static async Task RunUntilClosedAsync(WebApplication app, BrowserWindow window, LauncherLiveness liveness)
    {
        Console.WriteLine(window.Opened
            ? $"Opened in {window.Description}. Close the window, or press Ctrl+C here, to stop."
            : "No browser was opened: open that address in a browser. The designer stops a few seconds after its last window closes, or with Ctrl+C here.");

        var stopping = app.Lifetime.ApplicationStopping;
        using var watching = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var gone = liveness.WaitUntilGoneAsync(TimeSpan.FromSeconds(1), watching.Token);
        var exited = WatchWindowAsync(window, liveness, watching.Token);
        var finished = await Task.WhenAny(gone, exited, app.WaitForShutdownAsync());

        // Ctrl+C cancels the watches too, so whichever of them finished first says nothing then.
        var interrupted = stopping.IsCancellationRequested;
        var verdict = !interrupted && finished == gone ? await gone : (LivenessVerdict?)null;
        var windowExited = !interrupted && finished == exited;
        Console.WriteLine(verdict switch
        {
            LivenessVerdict.NeverConnected => "No designer window connected within a minute; stopping.",
            LivenessVerdict.AllClientsGone => "The designer window was closed; stopping.",
            _ when windowExited => "The designer window was closed; stopping.",
            _ => "Stopping.",
        });

        await watching.CancelAsync();

        // Ctrl+C, or a window that never connected: the window this launcher opened goes with it.
        if (interrupted || verdict == LivenessVerdict.NeverConnected) await window.CloseAsync();
        await app.StopAsync();
    }

    /// <summary>
    /// Completes when the window's own process has ended and the window is really gone. A process that ends at once,
    /// before any page connected, handed its window over to a browser already running, and one that ends while a page
    /// is still connected left the window somewhere else; either way the page's connection decides instead.
    /// </summary>
    private static async Task WatchWindowAsync(BrowserWindow window, LauncherLiveness liveness, CancellationToken cancellationToken)
    {
        if (window.Process is not { } process)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return;
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var handedOff = DateTimeOffset.UtcNow - window.Started < HandOffTime && !liveness.EverConnected;
        if (handedOff || liveness.OpenConnections > 0) await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The three endpoints the page talks to the launcher by: a heartbeat it probes once to learn it is served by the
    /// launcher, an event stream it keeps open while it lives, and a goodbye it sends as it goes. Each needs the token
    /// the page was opened with; anything without it gets a plain 404, like any static host would give.
    /// </summary>
    private static void MapLauncherEndpoints(WebApplication app, LauncherLiveness liveness, string token)
    {
        bool Authorized(HttpContext context) =>
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(context.Request.Query["token"].ToString()),
                Encoding.UTF8.GetBytes(token));

        app.MapGet("/launcher/heartbeat", (HttpContext context) =>
            Authorized(context)
                ? Results.Json(new { app = LauncherName, clients = liveness.OpenConnections })
                : Results.NotFound());

        app.MapPost("/launcher/bye", (HttpContext context) =>
        {
            if (!Authorized(context) || context.Request.Query["client"].ToString() is not { Length: > 0 } client) return Results.NotFound();
            liveness.Leave(client);
            return Results.NoContent();
        });

        app.MapGet("/launcher/events", async (HttpContext context) =>
        {
            if (!Authorized(context) || context.Request.Query["client"].ToString() is not { Length: > 0 } client)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await StreamAsync(context, client, liveness, app.Lifetime.ApplicationStopping);
        });
    }

    /// <summary>
    /// Holds a Server-Sent Events stream open for as long as the page keeps it, counting it as a live page meanwhile.
    /// A comment line goes down it every few seconds, so a connection that died without closing is noticed when the
    /// write fails; the launcher shutting down says so on the stream before closing it.
    /// </summary>
    private static async Task StreamAsync(HttpContext context, string client, LauncherLiveness liveness, CancellationToken stopping)
    {
        var response = context.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache, no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        context.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        liveness.Connected(client);
        using var both = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        try
        {
            await response.WriteAsync(": connected\n\n", both.Token);
            await response.Body.FlushAsync(both.Token);
            while (!both.IsCancellationRequested)
            {
                await Task.Delay(KeepAlive, both.Token);
                await response.WriteAsync(": still here\n\n", both.Token);
                await response.Body.FlushAsync(both.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // The page went, or the launcher is stopping.
        }
        finally
        {
            liveness.Disconnected(client);
        }

        if (stopping.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        {
            try
            {
                await response.WriteAsync("event: shutdown\ndata: stopping\n\n", CancellationToken.None);
                await response.Body.FlushAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Closed first; nothing to say.
            }
        }
    }
}
