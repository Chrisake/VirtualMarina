using System.Runtime.InteropServices;

namespace VirtualMarina.Designer.Desktop;

/// <summary>
/// The launcher's console, which on Windows is only there when asked for. The launcher is built as a Windows
/// application (<c>WinExe</c>), so starting it — from Explorer, a shortcut or a terminal — opens no console window of its
/// own; <c>--console</c> gets one back. Elsewhere a program has no console window to hide, and this does nothing.
/// </summary>
internal static partial class ConsoleWindow
{
    /// <summary>The console of the process that started this one, for <c>AttachConsole</c>.</summary>
    private const int AttachParentProcess = -1;

    private const uint IconWarning = 0x30;

    /// <summary>
    /// Gives the launcher a console: the one of the terminal it was started from, so its messages appear there and
    /// Ctrl+C in it stops the launcher, or a new console window when it was started any other way.
    /// </summary>
    /// <remarks>
    /// Call before anything writes to the console: .NET opens the console's streams on first use, so opened before
    /// this they would write to nowhere, and opened after it they write to the console it found. Output the starting
    /// process already redirected (to a file or a pipe) stays where it was sent.
    /// </remarks>
    public static void Show()
    {
        if (!OperatingSystem.IsWindows()) return;

        // Started with its output already sent somewhere — a pipe or a file — the output goes on there: taking a
        // console would take the standard handles over, and the output would never arrive where it was sent.
        if (IsRedirected(GetStdHandle(StdOutputHandle))) return;

        // Joining the starting terminal's console is also what lets Ctrl+C there stop the launcher.
        if (!AttachConsole(AttachParentProcess)) AllocConsole();
    }

    private const int StdOutputHandle = -11;
    private const uint FileTypeDisk = 1;
    private const uint FileTypePipe = 3;

    /// <summary>True when the handle is a file or a pipe: output someone else is collecting.</summary>
    private static bool IsRedirected(IntPtr handle)
    {
        // No handle at all, or INVALID_HANDLE_VALUE: nothing is collecting the output.
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;

        var type = GetFileType(handle);
        return type is FileTypeDisk or FileTypePipe;
    }

    /// <summary>
    /// Says something that matters when there is no console to say it in: in a message box on Windows, which does not
    /// hold up the launcher while it is open. Elsewhere, and with a console, it is written to the console as usual.
    /// </summary>
    public static void Tell(string message, bool hasConsole)
    {
        if (hasConsole || !OperatingSystem.IsWindows())
        {
            Console.WriteLine(message);
            return;
        }

        _ = Task.Run(() => MessageBoxW(IntPtr.Zero, message, "VirtualMarina Designer", IconWarning));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetFileType(IntPtr handle);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
