using System.Reflection;
using Xunit;

namespace VirtualMarina.TestSupport;

/// <summary>
/// Compares an assembly's public surface (<see cref="ApiSurface"/>) with the baseline approved for it, and on a
/// difference writes the new surface out next to the baseline and fails with what changed.
/// </summary>
/// <remarks>
/// The baselines are found in the source tree, not the build output: walking up from the test assembly to the
/// repository root works however the build maps its source paths (a CI build is deterministic and records
/// <c>/_/</c> in place of the real checkout directory, which <c>[CallerFilePath]</c> would then return).
/// </remarks>
public static class ApiBaseline
{
    /// <summary>
    /// Asserts the surface of <paramref name="assembly"/> matches <c>&lt;name&gt;.approved.txt</c> in
    /// <paramref name="folder"/>, a path relative to the repository root such as
    /// <c>tests/VirtualMarina.Core.Tests/ApiBaselines</c>.
    /// </summary>
    public static void AssertUnchanged(Assembly assembly, string folder) =>
        AssertUnchanged(ApiSurface.Of(assembly), assembly.GetName().Name!, Path.Combine(RepositoryRoot.Path, folder));

    /// <summary>Asserts an already-computed <paramref name="surface"/> matches the named baseline.</summary>
    public static void AssertUnchanged(string surface, string assemblyName, string folder)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Directory.CreateDirectory(folder);
        var approvedPath = Path.Combine(folder, $"{assemblyName}.approved.txt");
        var receivedPath = Path.Combine(folder, $"{assemblyName}.received.txt");

        var actual = Normalize(surface);
        var approved = File.Exists(approvedPath) ? Normalize(File.ReadAllText(approvedPath)) : null;

        if (approved == actual)
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);
        Assert.Fail(approved is null
            ? $"No baseline yet for {assemblyName}. Review {receivedPath} and rename it to {Path.GetFileName(approvedPath)}."
            : $"""
               The public API of {assemblyName} has changed.

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

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

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
