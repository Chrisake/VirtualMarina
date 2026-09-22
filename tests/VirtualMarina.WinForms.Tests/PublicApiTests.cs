using System.Reflection;
using System.Runtime.CompilerServices;
using VirtualMarina.Core.Tests;

namespace VirtualMarina.WinForms.Tests;

/// <summary>
/// The WinForms host control is what an application embeds, so its published surface is a promise too.
/// The check is the same as for the core library; see <c>Docs/16-compatibility.md</c>.
/// </summary>
public class PublicApiTests
{
    [Fact]
    public void WinFormsApi_MatchesTheApprovedBaseline()
    {
        var folder = Path.Combine(Path.GetDirectoryName(Here())!, "ApiBaselines");
        Directory.CreateDirectory(folder);

        var assembly = typeof(MarinaViewControl).Assembly;
        var approvedPath = Path.Combine(folder, $"{assembly.GetName().Name}.approved.txt");
        var receivedPath = Path.Combine(folder, $"{assembly.GetName().Name}.received.txt");

        var actual = Normalize(ApiSurface.Of(assembly));
        if (File.Exists(approvedPath) && Normalize(File.ReadAllText(approvedPath)) == actual)
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }

        File.WriteAllText(receivedPath, actual);
        Assert.Fail($"""
            The public API of {assembly.GetName().Name} has changed, or has no baseline yet.

            Review {receivedPath}. If the change only adds to the API, approve it by replacing
            {approvedPath} with it; otherwise it belongs in a new major version (Docs/16-compatibility.md).
            """);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string Here([CallerFilePath] string path = "") => path;
}
