using System.Reflection;
using VirtualMarina.Core.Api;
using VirtualMarina.TestSupport;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// The published API is a promise to the applications that integrate this library, so every change to it has to
/// be made on purpose. These tests compare the surface against a baseline checked in next to them; when they
/// fail, read the diff and decide whether the change is allowed (see Docs/16-compatibility.md), then approve it.
/// </summary>
/// <remarks>
/// The baseline records the number of every enum member too, because those numbers end up in host databases and
/// in saved files: reshuffling them, or inserting a member in the middle, shows up as a changed line.
/// </remarks>
public class PublicApiTests
{
    [Fact]
    public void CoreApi_MatchesTheApprovedBaseline() => AssertApiUnchanged(typeof(MarinaVisualizer).Assembly);

    [Fact]
    public void OpenGlApi_MatchesTheApprovedBaseline() =>
        AssertApiUnchanged(typeof(global::VirtualMarina.Rendering.OpenGL.OpenGlSceneRenderer).Assembly);

    private static void AssertApiUnchanged(Assembly assembly) =>
        ApiBaseline.AssertUnchanged(assembly, Path.Combine("tests", "VirtualMarina.Core.Tests", "ApiBaselines"));
}
