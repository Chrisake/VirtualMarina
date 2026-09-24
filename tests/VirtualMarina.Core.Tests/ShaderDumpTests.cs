using System.Reflection;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Writes every shader <see cref="ShaderSources"/> can produce, in every dialect, to .vert/.frag files so a real
/// GLSL compiler can check them. The unit tests only look for substrings; a shader that does not compile would
/// otherwise pass them and fail only on a user's GPU.
/// </summary>
/// <remarks>
/// <para>
/// Set <c>VM_SHADER_DUMP_DIR</c> to a directory and run this test; CI then runs <c>glslangValidator</c> on each
/// file (.github/workflows/ci.yml). Without the variable the test still checks that shaders are found and that each
/// one starts with the #version line its dialect needs, so the enumeration cannot silently find nothing.
/// </para>
/// <para>
/// Shaders are found by reflection rather than listed, so a new or renamed shader is checked without touching this
/// file: any public or internal static method of ShaderSources taking a <see cref="ShaderDialect"/> and returning a string. Its name
/// must end in Vertex or Fragment, which is how the stage (and the file extension glslang reads it by) is chosen.
/// </para>
/// </remarks>
public class ShaderDumpTests
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    public static IEnumerable<(string Name, ShaderDialect Dialect, string Stage, string Source)> AllShaders()
    {
        var generators = typeof(ShaderSources).GetMethods(AnyStatic)
            // Private helpers (the version header) take a dialect too, but are pieces of a shader, not shaders.
            .Where(m => !m.IsPrivate && m.ReturnType == typeof(string)
                && m.GetParameters() is [{ ParameterType: var type }] && type == typeof(ShaderDialect))
            .OrderBy(m => m.Name, StringComparer.Ordinal);

        foreach (var method in generators)
        {
            foreach (var dialect in Enum.GetValues<ShaderDialect>())
            {
                var source = (string)method.Invoke(null, [dialect])!;
                yield return (method.Name, dialect, StageOf(method.Name), source);
            }
        }
    }

    private static string StageOf(string name) =>
        name.EndsWith("Vertex", StringComparison.Ordinal) ? "vert"
        : name.EndsWith("Fragment", StringComparison.Ordinal) ? "frag"
        : throw new InvalidOperationException(
            $"ShaderSources.{name} produces a shader, but its name does not end in Vertex or Fragment, so its stage is unknown.");

    [Fact]
    public void EveryShader_IsFound_AndCarriesItsDialectsVersionLine()
    {
        var shaders = AllShaders().ToList();

        // At least a vertex and a fragment shader for each program; far more in practice.
        Assert.True(shaders.Count(s => s.Stage == "vert") >= 2, "no vertex shaders found");
        Assert.True(shaders.Count(s => s.Stage == "frag") >= 2, "no fragment shaders found");
        Assert.All(shaders, shader => Assert.StartsWith(
            shader.Dialect == ShaderDialect.WebGL2 ? "#version 300 es\n" : "#version 330 core\n",
            shader.Source,
            StringComparison.Ordinal));

        var directory = Environment.GetEnvironmentVariable("VM_SHADER_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        Directory.CreateDirectory(directory);
        foreach (var (name, dialect, stage, source) in shaders)
        {
            // e.g. ModelVertex.DesktopGL33.vert: glslangValidator takes the stage from the last extension.
            File.WriteAllText(Path.Combine(directory, $"{name}.{dialect}.{stage}"), source);
        }
    }
}
