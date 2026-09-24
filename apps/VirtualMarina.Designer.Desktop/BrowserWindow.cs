using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace VirtualMarina.Designer.Desktop;

/// <summary>
/// The window the designer opened in: a Chromium-family browser in application mode when there is one, the default
/// browser otherwise. <see cref="Process"/> is the browser's own process when it could be started on a profile of its
/// own and so waited on; null when the window belongs to a browser that cannot be (a Flatpak or snap, one already
/// running, the default browser).
/// </summary>
/// <param name="Description">What was opened, for the console.</param>
/// <param name="Process">The window's process, or null when it cannot be tracked.</param>
/// <param name="Opened">False when nothing could be opened at all.</param>
/// <remarks>The launcher disposes <see cref="Process"/> when it is done with the window.</remarks>
internal sealed record BrowserWindow(string Description, Process? Process, bool Opened)
{
    /// <summary>Where <c>kill</c> is found, by its full path so no other program of that name is started instead.</summary>
    private static readonly string[] KillCommands = ["/bin/kill", "/usr/bin/kill"];

    /// <summary>When the window's process was started.</summary>
    public DateTimeOffset Started { get; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Closes the window this launcher opened, when it is one it can close: asks it to close first, the way the user
    /// would, and ends it if it has not gone within a few seconds. A window in someone else's browser is left alone.
    /// </summary>
    public async Task CloseAsync()
    {
        if (Process is not { HasExited: false } process) return;
        try
        {
            if (OperatingSystem.IsWindows()) process.CloseMainWindow();
            else Signal(process.Id);

            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await process.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
        }
        catch (InvalidOperationException)
        {
            // Gone already.
        }
    }

    /// <summary>SIGTERM, which a Chromium browser treats as "close every window and quit", saving its session tidily.</summary>
    private static void Signal(int processId)
    {
        if (KillCommands.FirstOrDefault(File.Exists) is not { } command) return;
        using var kill = BrowserLauncher.TryStart(new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            ArgumentList = { "-TERM", processId.ToString(CultureInfo.InvariantCulture) },
        });
        kill?.WaitForExit(2000);
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Gone already, or not ours to end.
        }
    }
}

/// <summary>
/// Finds a browser that can show the designer as an app window and opens it. Chromium-family browsers (Chrome, Edge,
/// Chromium, Brave) have an application mode — a window with no tabs or address bar — and are tried first, in that
/// order; failing all of them, the page opens in the default browser.
/// </summary>
internal static class BrowserLauncher
{
    /// <summary>Environment variable naming a browser executable to use instead of looking for one.</summary>
    public const string BrowserVariable = "VIRTUALMARINA_BROWSER";

    /// <summary>A browser executable and how it can be started.</summary>
    /// <param name="Name">What to call it on the console.</param>
    /// <param name="Path">The executable.</param>
    /// <param name="Dedicated">True when it can be started on a profile of its own and waited on.</param>
    private sealed record Candidate(string Name, string Path, bool Dedicated);

    /// <summary>Opens the page, in an app window if possible.</summary>
    /// <param name="url">The page.</param>
    public static BrowserWindow Open(string url)
    {
        foreach (var candidate in Candidates())
        {
            var window = candidate.Dedicated ? StartDedicated(candidate, url) : StartShared(candidate.Name, candidate.Path, ["--app=" + url]);
            if (window is not null) return window;
        }

        if (OperatingSystem.IsLinux() && Flatpak() is { } flatpak)
        {
            // A Flatpak browser hands the window to its own sandboxed instance, which cannot be waited on.
            foreach (var (name, id) in FlatpakBrowsers)
            {
                if (!FlatpakHas(flatpak, id)) continue;
                var window = StartShared($"{name} (Flatpak)", flatpak, ["run", id, "--app=" + url]);
                if (window is not null) return window;
            }
        }

        // No app mode to be had: an ordinary tab in whatever the user browses with.
        var shell = TryStart(new ProcessStartInfo(url) { UseShellExecute = true });
        shell?.Dispose();
        return new BrowserWindow("the default browser", null, shell is not null);
    }

    /// <summary>Starts a process, or returns null when the executable is missing or refuses to start.</summary>
    internal static Process? TryStart(ProcessStartInfo info)
    {
        try
        {
            return Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts the browser on a profile of its own, so the window is its own process, closing it ends that process, and
    /// the user's everyday browsing is not mixed into it. The profile is kept between launches (see <see cref="Profiles"/>).
    /// </summary>
    private static BrowserWindow? StartDedicated(Candidate candidate, string url)
    {
        if (Profiles.For(candidate.Name) is not { } profile) return StartShared(candidate.Name, candidate.Path, ["--app=" + url]);

        var process = TryStart(new ProcessStartInfo(candidate.Path)
        {
            UseShellExecute = false,
            ArgumentList =
            {
                "--app=" + url,
                "--user-data-dir=" + profile,
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-session-crashed-bubble",
            },
        });
        return process is null ? null : new BrowserWindow(candidate.Name, process, Opened: true);
    }

    /// <summary>Starts a browser that cannot be waited on: its window lives in a process of its own choosing.</summary>
    private static BrowserWindow? StartShared(string name, string executable, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = TryStart(info);
        if (process is null) return null;
        process.Dispose();
        return new BrowserWindow(name, null, Opened: true);
    }

    /// <summary>Every browser worth trying on this system, most preferred first.</summary>
    private static IEnumerable<Candidate> Candidates()
    {
        if (Environment.GetEnvironmentVariable(BrowserVariable) is { Length: > 0 } chosen && File.Exists(chosen))
        {
            yield return new Candidate(Path.GetFileNameWithoutExtension(chosen), chosen, Dedicated: true);
        }

        IEnumerable<Candidate> found =
            OperatingSystem.IsWindows() ? WindowsBrowsers()
            : OperatingSystem.IsMacOS() ? MacBrowsers()
            : LinuxBrowsers();
        foreach (var candidate in found) yield return candidate;
    }

    private static IEnumerable<Candidate> WindowsBrowsers()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        (string Name, string Relative)[] browsers =
        [
            ("Google Chrome", @"Google\Chrome\Application\chrome.exe"),
            ("Microsoft Edge", @"Microsoft\Edge\Application\msedge.exe"),
            ("Chromium", @"Chromium\Application\chrome.exe"),
            ("Brave", @"BraveSoftware\Brave-Browser\Application\brave.exe"),
        ];

        foreach (var (name, relative) in browsers)
        {
            foreach (var root in new[] { programFiles, programFilesX86, local })
            {
                if (root.Length == 0) continue;
                var path = Path.Combine(root, relative);
                if (File.Exists(path)) yield return new Candidate(name, path, Dedicated: true);
            }
        }
    }

    private static IEnumerable<Candidate> MacBrowsers()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        (string Name, string Bundle, string Executable)[] browsers =
        [
            ("Google Chrome", "Google Chrome.app", "Google Chrome"),
            ("Microsoft Edge", "Microsoft Edge.app", "Microsoft Edge"),
            ("Chromium", "Chromium.app", "Chromium"),
            ("Brave", "Brave Browser.app", "Brave Browser"),
        ];

        foreach (var (name, bundle, executable) in browsers)
        {
            foreach (var applications in new[] { "/Applications", Path.Combine(home, "Applications") })
            {
                var path = Path.Combine(applications, bundle, "Contents", "MacOS", executable);
                if (File.Exists(path)) yield return new Candidate(name, path, Dedicated: true);
            }
        }
    }

    private static IEnumerable<Candidate> LinuxBrowsers()
    {
        (string Name, string Executable)[] browsers =
        [
            ("Google Chrome", "google-chrome"),
            ("Google Chrome", "google-chrome-stable"),
            ("Microsoft Edge", "microsoft-edge"),
            ("Microsoft Edge", "microsoft-edge-stable"),
            ("Chromium", "chromium"),
            ("Chromium", "chromium-browser"),
            ("Brave", "brave-browser"),
            ("Brave", "brave"),
        ];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, executable) in browsers)
        {
            if (OnPath(executable) is not { } path || !seen.Add(path)) continue;

            // A snap is confined to its own corner of the home folder, so it cannot be given a profile of ours; it is
            // started like a Flatpak, and the page itself tells the launcher when its window closes.
            yield return new Candidate(name, path, Dedicated: !IsSnap(path));
        }

        foreach (var (name, snap) in new[] { ("Chromium (snap)", "/snap/bin/chromium"), ("Brave (snap)", "/snap/bin/brave") })
        {
            if (File.Exists(snap) && seen.Add(snap)) yield return new Candidate(name, snap, Dedicated: false);
        }
    }

    private static readonly (string Name, string Id)[] FlatpakBrowsers =
    [
        ("Google Chrome", "com.google.Chrome"),
        ("Microsoft Edge", "com.microsoft.Edge"),
        ("Chromium", "org.chromium.Chromium"),
        ("Brave", "com.brave.Browser"),
    ];

    /// <summary>
    /// True for a snap: a file under /snap, or one of the wrapper scripts some distributions put on the PATH that start
    /// the snap (Ubuntu's chromium-browser, for one).
    /// </summary>
    private static bool IsSnap(string path)
    {
        try
        {
            var real = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
            if (real.StartsWith("/snap/", StringComparison.Ordinal)) return true;

            using var file = File.OpenRead(real);
            var head = new byte[4096];
            var read = file.Read(head, 0, head.Length);
            var text = System.Text.Encoding.UTF8.GetString(head, 0, read);
            return text.StartsWith("#!", StringComparison.Ordinal) && text.Contains("/snap/", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Flatpak() => OnPath("flatpak");

    private static bool FlatpakHas(string flatpak, string id)
    {
        using var probe = TryStart(new ProcessStartInfo(flatpak)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "info", id },
        });

        return probe is not null && probe.WaitForExit(4000) && probe.ExitCode == 0;
    }

    private static string? OnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executable))
            .FirstOrDefault(File.Exists);
    }
}

/// <summary>
/// The browser profiles the launcher starts its windows on: one per browser, kept between launches under the user's
/// own application data (not the shared temp folder), readable by the user alone.
/// </summary>
/// <remarks>
/// Earlier versions made a throwaway profile per launch under the temp folder and swept away ones that looked
/// abandoned, which in a temp folder other users share meant a predictable name and deleting folders by their name
/// alone. Nothing is deleted now: the profile is the user's, and stays.
/// </remarks>
internal static class Profiles
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The profile folder for a browser, created if need be; null when there is no private place to keep it (no home
    /// folder, or a folder there that is not the user's own), in which case the browser is started without one.
    /// </summary>
    /// <param name="browser">The browser's name; each browser has its own, since their profiles are not interchangeable.</param>
    public static string? For(string browser)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(root)) return null;

        var folder = root;
        foreach (var part in new[] { "VirtualMarina", "Designer", "BrowserProfiles", Safe(browser) })
        {
            folder = Path.Combine(folder, part);
            if (!EnsurePrivate(folder)) return null;
        }

        return folder;
    }

    /// <summary>
    /// Creates a folder only its owner can enter, or checks that an existing one is a real folder (not a link somewhere
    /// else) and puts its permissions right. Changing the permissions is only allowed to the folder's owner, so a folder
    /// that belongs to someone else fails here and is not used.
    /// </summary>
    private static bool EnsurePrivate(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            if (info.Exists && info.LinkTarget is not null) return false;

            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);   // under %LOCALAPPDATA%, which is the user's alone already
                return true;
            }

            if (!info.Exists) Directory.CreateDirectory(folder, OwnerOnly);
            else if (File.GetUnixFileMode(folder) != OwnerOnly) File.SetUnixFileMode(folder, OwnerOnly);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string Safe(string name) => new(name.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());
}
