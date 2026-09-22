using System.Reflection;
using System.Runtime.CompilerServices;
using VirtualMarina.Core.Api;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The published API is a promise to the applications that integrate this library, so every change to it has to
/// be made on purpose. These tests compare the surface against a baseline checked in next to them; when they
/// fail, read the diff and decide whether the change is allowed (see Docs/16-compatibility.md), then approve it.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void CoreApi_MatchesTheApprovedBaseline() => AssertApiUnchanged(typeof(MarinaVisualizer).Assembly);

    [Fact]
    public void BlazorApi_MatchesTheApprovedBaseline() =>
        AssertApiUnchanged(typeof(global::VirtualMarina.Blazor.MarinaView).Assembly);

    [Fact]
    public void OpenGlApi_MatchesTheApprovedBaseline() =>
        AssertApiUnchanged(typeof(global::VirtualMarina.Rendering.OpenGL.OpenGlSceneRenderer).Assembly);

    /// <summary>
    /// The baseline records the number of every enum member, because those numbers end up in host databases and
    /// in saved files. Reshuffling them, or inserting a member in the middle, shows up here as a changed line and
    /// must be rejected: a new member is appended with the next free number.
    /// </summary>
    [Fact]
    public void EnumNumbers_AreRecordedInTheBaseline()
    {
        var surface = ApiSurface.Of(typeof(MarinaVisualizer).Assembly);
        Assert.Contains("BerthStatus", surface);
        Assert.Contains("Occupied = 1", surface);
        Assert.Contains("TemporarilyFree = 3", surface);
    }

    /// <summary>Compares an assembly's surface with its baseline, writing the new one out when they differ.</summary>
    internal static void AssertApiUnchanged(Assembly assembly, [CallerFilePath] string testFilePath = "")
    {
        var folder = Path.Combine(Path.GetDirectoryName(testFilePath)!, "ApiBaselines");
        Directory.CreateDirectory(folder);
        var approvedPath = Path.Combine(folder, $"{assembly.GetName().Name}.approved.txt");
        var receivedPath = Path.Combine(folder, $"{assembly.GetName().Name}.received.txt");

        var actual = Normalize(ApiSurface.Of(assembly));
        var approved = File.Exists(approvedPath) ? Normalize(File.ReadAllText(approvedPath)) : null;

        if (approved == actual)
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);
        Assert.Fail(approved is null
            ? $"No baseline yet for {assembly.GetName().Name}. Review {receivedPath} and rename it to {Path.GetFileName(approvedPath)}."
            : $"""
               The public API of {assembly.GetName().Name} has changed.

               {Summarize(approved, actual)}

               Removing or changing anything that was already published breaks the applications using it. If the
               change is additive (a new type, a new member, a new optional parameter, a new enum member with the
               next free number), approve it by replacing
                 {approvedPath}
               with
                 {receivedPath}
               Otherwise it belongs in a new major version — see Docs/16-compatibility.md.
               """);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    /// <summary>The added and removed lines, so the failure message says what actually changed.</summary>
    private static string Summarize(string approved, string actual)
    {
        var before = approved.Split('\n');
        var after = actual.Split('\n');
        var removed = before.Except(after).Where(line => line.Trim().Length > 0).ToArray();
        var added = after.Except(before).Where(line => line.Trim().Length > 0).ToArray();

        var report = new List<string>();
        report.AddRange(removed.Take(25).Select(line => "- " + line.Trim()));
        if (removed.Length > 25) report.Add($"  ... and {removed.Length - 25} more removed");
        report.AddRange(added.Take(25).Select(line => "+ " + line.Trim()));
        if (added.Length > 25) report.Add($"  ... and {added.Length - 25} more added");
        return report.Count == 0 ? "(only formatting differs)" : string.Join("\n", report);
    }
}
