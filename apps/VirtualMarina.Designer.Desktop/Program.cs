using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace VirtualMarina.Designer.Desktop;

/// <summary>
/// Runs the Blazor designer as a windowed app: it serves the WebAssembly build on a local port and opens Chrome in
/// application mode — a chromeless window with no tabs or address bar — pointed at it. Because it is the ordinary
/// browser engine loading the ordinary served app, WebGL and the synchronous JS interop the renderer relies on work
/// exactly as they do in a tab; only the window has no browser furniture.
/// </summary>
internal static class Program
{
    /// <summary>Chrome executables looked for on the PATH on Linux.</summary>
    private static readonly string[] LinuxChrome = { "google-chrome", "google-chrome-stable", "chromium", "chromium-browser" };

    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();     // a launcher, not a server: keep the console quiet
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));   // any free loopback port
        builder.WebHost.UseStaticWebAssets();  // serve the client's assets whatever the environment, not just in Development

        var app = builder.Build();
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
        await app.StartAsync();

        var url = app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.First();

        var launch = OpenAppWindow(url);
        using var window = launch.Process;
        Console.WriteLine($"VirtualMarina Designer is running at {url}");
        Console.WriteLine(launch.Launched
            ? "Close the app window or press Ctrl+C here to stop."
            : "Google Chrome was not found — open that address in any browser, then press Ctrl+C here to stop.");

        // A dedicated Chrome instance can be waited on, so closing the window stops the app; otherwise (an already
        // running Chrome, or none found) the launcher stays up until Ctrl+C.
        if (window is not null) await Task.WhenAny(window.WaitForExitAsync(), app.WaitForShutdownAsync());
        else await app.WaitForShutdownAsync();

        await app.StopAsync();
    }

    /// <summary>
    /// Opens the app in a Chrome application-mode window. <c>Process</c> is the window's process when a dedicated
    /// instance was started (so the caller can quit when it closes) and null otherwise; <c>Launched</c> says whether
    /// Chrome was opened at all, so an untracked launch (Flatpak) is still told apart from Chrome not being found.
    /// </summary>
    private static (Process? Process, bool Launched) OpenAppWindow(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var window = WindowsChromePaths().Where(File.Exists).Select(path => StartDedicated(path, url)).FirstOrDefault(p => p is not null);
            return (window, window is not null);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            const string chrome = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";
            var window = File.Exists(chrome) ? StartDedicated(chrome, url) : null;
            return (window, window is not null);
        }

        foreach (var exe in LinuxChrome)
        {
            if (OnPath(exe) is { } path)
            {
                var window = StartDedicated(path, url);
                return (window, window is not null);
            }
        }

        // Chrome installed as a Flatpak: launch it, but it delegates to its own sandboxed instance which cannot be
        // waited on, so it is fire-and-forget and the launcher waits for Ctrl+C instead.
        if (OnPath("flatpak") is { } flatpak && FlatpakChromePresent(flatpak))
        {
            var started = TryStart(new ProcessStartInfo(flatpak) { UseShellExecute = false, ArgumentList = { "run", "com.google.Chrome", $"--app={url}" } });
            started?.Dispose();
            return (null, started is not null);
        }

        return (null, false);
    }

    /// <summary>Starts a dedicated Chrome window on its own throwaway profile, so closing it ends this launcher.</summary>
    private static Process? StartDedicated(string chrome, string url)
    {
        var profile = Path.Combine(Path.GetTempPath(), $"VirtualMarinaDesigner-{Environment.ProcessId}");
        return TryStart(new ProcessStartInfo(chrome)
        {
            UseShellExecute = false,
            ArgumentList = { $"--app={url}", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check" },
        });
    }

    private static Process? TryStart(ProcessStartInfo info)
    {
        try
        {
            return Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool FlatpakChromePresent(string flatpak)
    {
        var probe = TryStart(new ProcessStartInfo(flatpak)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "info", "com.google.Chrome" },
        });

        if (probe is null) return false;
        using (probe)
        {
            return probe.WaitForExit(4000) && probe.ExitCode == 0;
        }
    }

    private static string? OnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator)
            .Select(dir => Path.Combine(dir, exe))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> WindowsChromePaths()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe");
    }
}
