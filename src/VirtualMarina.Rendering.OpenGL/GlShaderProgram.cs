using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace VirtualMarina.Rendering.OpenGL;

/// <summary>Compiled and linked GLSL program with every uniform location looked up once, at link time.</summary>
/// <remarks>
/// Look a location up with <see cref="Location"/> when setting up, keep the int, and set values through it: nothing is
/// looked up by name while drawing.
/// </remarks>
internal sealed class GlShaderProgram : IDisposable
{
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);
    private readonly float[] _matrix = new float[16];
    private bool _disposed;

    public GlShaderProgram(string name, string vertexSource, string fragmentSource)
    {
        var vertex = Compile(name, ShaderType.VertexShader, vertexSource);
        int fragment;
        try
        {
            fragment = Compile(name, ShaderType.FragmentShader, fragmentSource);
        }
        catch (InvalidOperationException)
        {
            GL.DeleteShader(vertex);
            throw;
        }

        Handle = GL.CreateProgram();
        GL.AttachShader(Handle, vertex);
        GL.AttachShader(Handle, fragment);
        GL.LinkProgram(Handle);
        GL.DetachShader(Handle, vertex);
        GL.DetachShader(Handle, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);

        GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out var linked);
        if (linked == 0)
        {
            var log = GL.GetProgramInfoLog(Handle);
            GL.DeleteProgram(Handle);
            throw new InvalidOperationException($"Linking shader program '{name}' failed: {log}");
        }

        GL.GetProgram(Handle, GetProgramParameterName.ActiveUniforms, out var count);
        for (var i = 0; i < count; i++)
        {
            var uniform = GL.GetActiveUniform(Handle, i, out _, out _);
            _locations[uniform] = GL.GetUniformLocation(Handle, uniform);
        }
    }

    public int Handle { get; }

    public void Use() => GL.UseProgram(Handle);

    /// <summary>The location of an active uniform, or -1 when the program has none by that name (setting -1 does nothing).</summary>
    public int Location(string name) => _locations.TryGetValue(name, out var location) ? location : -1;

    public static void Set(int location, float value)
    {
        if (location >= 0) GL.Uniform1(location, value);
    }

    public static void Set(int location, int value)
    {
        if (location >= 0) GL.Uniform1(location, value);
    }

    public static void Set(int location, Vector2 value)
    {
        if (location >= 0) GL.Uniform2(location, value.X, value.Y);
    }

    public static void Set(int location, Vector3 value)
    {
        if (location >= 0) GL.Uniform3(location, value.X, value.Y, value.Z);
    }

    public static void Set(int location, Vector4 value)
    {
        if (location >= 0) GL.Uniform4(location, value.X, value.Y, value.Z, value.W);
    }

    /// <summary>
    /// Uploads a System.Numerics matrix. Row-major M11..M44 order read as column-major is exactly the
    /// transpose GLSL's column-vector convention needs, so no transpose flag is required.
    /// </summary>
    public void Set(int location, in Matrix4x4 m)
    {
        if (location < 0) return;

        _matrix[0] = m.M11; _matrix[1] = m.M12; _matrix[2] = m.M13; _matrix[3] = m.M14;
        _matrix[4] = m.M21; _matrix[5] = m.M22; _matrix[6] = m.M23; _matrix[7] = m.M24;
        _matrix[8] = m.M31; _matrix[9] = m.M32; _matrix[10] = m.M33; _matrix[11] = m.M34;
        _matrix[12] = m.M41; _matrix[13] = m.M42; _matrix[14] = m.M43; _matrix[15] = m.M44;
        GL.UniformMatrix4(location, 1, false, _matrix);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GL.DeleteProgram(Handle);
    }

    private static int Compile(string programName, ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled == 0)
        {
            var log = GL.GetShaderInfoLog(shader);
            GL.DeleteShader(shader);
            throw new InvalidOperationException($"Compiling {type} for '{programName}' failed: {log}");
        }

        return shader;
    }
}
