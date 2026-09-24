namespace VirtualMarina.Core.Rendering;

/// <summary>GLSL flavor to generate.</summary>
public enum ShaderDialect
{
    /// <summary>Desktop OpenGL 3.3 core profile (GLSL 330).</summary>
    DesktopGL33 = 0,

    /// <summary>WebGL 2 / OpenGL ES 3.0 (GLSL ES 300).</summary>
    WebGL2 = 1,
}

/// <summary>
/// Shared GLSL for every OpenGL-family backend. The bodies are written in the common subset of GLSL 330
/// and GLSL ES 300; only the version/precision header differs. That keeps the waves, shading and GPU
/// animation identical on desktop and in the browser.
/// </summary>
/// <remarks>
/// <para>Mesh attributes: location 0 = position, 1 = normal, 2 = color.</para>
/// <para>
/// Frame uniforms: uView, uProjection, uCameraPos, uTime, uSunDirection, uSunColor, uAmbientColor, uSpecularStrength,
/// uShininess, uSkyColor, uFogColor, uFogDensity, uWaveAmplitude, uWaveFrequency, uWaveSpeed and uFloatMotion (models);
/// the water adds uWaterDeep, uWaterShallow, uSkyReflection, uRipples, uSunGlints, uWaterCenter and uDetailRadius.
/// </para>
/// <para>
/// Per object, the instanced model program (<see cref="InstancedModelVertex"/>) reads instance attributes 3 to 7 laid out
/// as <see cref="InstanceData"/> describes; the one-object-per-draw program (<see cref="ModelVertex"/>) reads the uniforms
/// uModel, uTint, uEmissive, uDesaturation, uAnimation and uPhase instead.
/// </para>
/// <para>
/// Reference image: uView, uProjection, uImageMin, uImageMax, uImageHeight, uImage (texture unit 0) and uOpacity.
/// </para>
/// </remarks>
public static class ShaderSources
{
    /// <summary>
    /// Vertex shader for one object per draw call: placement plus GPU animations (floating, spin, above-waves lift), with the
    /// object's values in uniforms. Backends that draw instanced use <see cref="InstancedModelVertex"/> instead.
    /// </summary>
    public static string ModelVertex(ShaderDialect dialect) => Header(dialect) + WaveFunctions + ModelPlacement + ModelVertexBody;

    /// <summary>Fragment shader for <see cref="ModelVertex"/>: lighting, tint, emissive highlight, desaturation and fog.</summary>
    public static string ModelFragment(ShaderDialect dialect) => Header(dialect) + WaveFunctions + FogFunction + ModelShading + ModelFragmentBody;

    /// <summary>
    /// Vertex shader for instanced drawing: the same placement and animations as <see cref="ModelVertex"/>, with each
    /// instance's transform, tint and parameters read from instance attributes 3 to 7 (see <see cref="InstanceData"/>).
    /// </summary>
    public static string InstancedModelVertex(ShaderDialect dialect) => Header(dialect) + WaveFunctions + ModelPlacement + InstancedVertexBody;

    /// <summary>Fragment shader for <see cref="InstancedModelVertex"/>: the same shading as <see cref="ModelFragment"/>.</summary>
    public static string InstancedModelFragment(ShaderDialect dialect) => Header(dialect) + WaveFunctions + FogFunction + ModelShading + InstancedFragmentBody;

    /// <summary>Vertex shader for the water grid: displaces vertices by the wave function.</summary>
    public static string WaterVertex(ShaderDialect dialect) => Header(dialect) + WaveFunctions + WaterVertexBody;

    /// <summary>Fragment shader for the water: fresnel sky reflection, ripples, sun glints and fog.</summary>
    public static string WaterFragment(ShaderDialect dialect) => Header(dialect) + WaveFunctions + FogFunction + WaterFragmentBody;

    /// <summary>
    /// Vertex shader for the reference image: attribute 0 is a unit-square corner (vec2, 0..1); uniforms <c>uImageMin</c>,
    /// <c>uImageMax</c> (plan X/Z corners) and <c>uImageHeight</c> place it, and the corner doubles as the texture coordinate.
    /// </summary>
    public static string ImageVertex(ShaderDialect dialect) => Header(dialect) + ImageVertexBody;

    /// <summary>Fragment shader for the reference image: samples <c>uImage</c> (texture unit 0) with <c>uOpacity</c>.</summary>
    public static string ImageFragment(ShaderDialect dialect) => Header(dialect) + ImageFragmentBody;

    /// <summary>The unit square drawn by the image program: two triangles as 6 (x, y) corners.</summary>
    public static readonly float[] ImageQuadCorners = { 0f, 0f, 1f, 0f, 1f, 1f, 0f, 0f, 1f, 1f, 0f, 1f };

    private static string Header(ShaderDialect dialect) => dialect switch
    {
        ShaderDialect.DesktopGL33 => "#version 330 core\n",
        ShaderDialect.WebGL2 => "#version 300 es\nprecision highp float;\nprecision highp int;\n",
        _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
    };

    /// <summary>Relative amplitudes of the four wave components (multiplied by <see cref="WaterSettings.WaveAmplitude"/>).</summary>
    internal static readonly float[] WaveComponentAmplitudes = { 1.0f, 0.6f, 0.35f, 0.22f };

    /// <summary>
    /// Highest the water surface can ever rise, as a multiple of <see cref="WaterSettings.WaveAmplitude"/>: the sum of the
    /// component amplitudes. The rendered grid interpolates between vertex heights, so it can't exceed this either.
    /// </summary>
    public static float MaxWaveHeightFactor => WaveComponentAmplitudes.Sum();

    private static string GlslFloat(float value) => value.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string WaveFunctions = $$"""

        uniform float uTime;
        uniform float uWaveAmplitude;
        uniform float uWaveFrequency;
        uniform float uWaveSpeed;

        // Upper bound of vmWaveHeight (and of the interpolated water grid) for the current amplitude.
        float vmMaxWaveHeight()
        {
            return {{GlslFloat(MaxWaveHeightFactor)}} * abs(uWaveAmplitude);
        }

        // Sum of four directional sine waves. Returns height; writes the analytic XZ gradient.
        float vmWaveHeight(vec2 p, out vec2 grad)
        {
            vec2 dirs[4] = vec2[4](vec2(0.98, 0.20), vec2(-0.29, 0.96), vec2(0.71, -0.70), vec2(-0.91, -0.41));
            float amps[4] = float[4]({{string.Join(", ", WaveComponentAmplitudes.Select(GlslFloat))}});
            float freqs[4] = float[4](0.22, 0.37, 0.61, 1.05);
            float speeds[4] = float[4](0.9, 1.25, 1.6, 2.2);
            float h = 0.0;
            grad = vec2(0.0);
            for (int i = 0; i < 4; i++)
            {
                float f = freqs[i] * uWaveFrequency;
                float a = amps[i] * uWaveAmplitude;
                float phase = dot(dirs[i], p) * f + uTime * speeds[i] * uWaveSpeed;
                h += a * sin(phase);
                grad += dirs[i] * (a * f * cos(phase));
            }
            return h;
        }

        """;

    private const string FogFunction = """

        uniform vec3 uCameraPos;
        uniform vec3 uFogColor;
        uniform float uFogDensity;

        vec3 vmApplyFog(vec3 color, vec3 worldPos)
        {
            float d = length(uCameraPos - worldPos) * uFogDensity;
            float fog = 1.0 - exp(-d * d);
            return mix(color, uFogColor, clamp(fog, 0.0, 1.0));
        }

        """;

    /// <summary>Places a mesh vertex in the world, with the GPU animations, for both model programs.</summary>
    private const string ModelPlacement = """

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;

        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform float uFloatMotion;

        out vec3 vWorldPos;
        out vec3 vNormal;
        out vec3 vColor;

        mat3 vmRotX(float a) { float c = cos(a); float s = sin(a); return mat3(1.0, 0.0, 0.0, 0.0, c, s, 0.0, -s, c); }
        mat3 vmRotY(float a) { float c = cos(a); float s = sin(a); return mat3(c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c); }
        mat3 vmRotZ(float a) { float c = cos(a); float s = sin(a); return mat3(c, s, 0.0, -s, c, 0.0, 0.0, 0.0, 1.0); }

        void vmPlace(mat4 model, int animation, float phase)
        {
            vec3 p = aPosition;
            vec3 n = aNormal;
            float lift = 0.0;
            vec3 origin = model[3].xyz;

            if ((animation & 1) != 0)
            {
                vec2 grad;
                float h = vmWaveHeight(origin.xz, grad);
                float t = uTime * uWaveSpeed;
                float roll = (sin(t * 0.8 + phase) * 0.02 + clamp(grad.x, -0.2, 0.2) * 0.3) * uFloatMotion;
                float pitch = (sin(t * 0.63 + phase * 1.7) * 0.012 + clamp(grad.y, -0.2, 0.2) * 0.2) * uFloatMotion;
                mat3 r = vmRotZ(roll) * vmRotX(pitch);
                p = r * p;
                n = r * n;
                lift += h * clamp(uFloatMotion, 0.0, 1.0);
            }

            if ((animation & 8) != 0)
            {
                // Always above the highest possible wave, so water can never cover the object from a camera above it.
                lift += vmMaxWaveHeight();
            }

            if ((animation & 2) != 0)
            {
                mat3 r = vmRotY(uTime * 1.5 + phase);
                p = r * p;
                n = r * n;
                lift += sin(uTime * 2.5 + phase) * 0.35;
            }

            vec4 world = model * vec4(p, 1.0);
            world.y += lift;

            // The normal matrix is the cofactor matrix of the model's 3x3 part: the inverse transpose without the division
            // by the determinant, which only scales it (and flips it for a mirroring transform). Cross products are cheap,
            // and a flattened shadow, which has no inverse, still gets a finite normal (it is drawn unlit anyway).
            mat3 m = mat3(model);
            mat3 cofactor = mat3(cross(m[1], m[2]), cross(m[2], m[0]), cross(m[0], m[1]));
            vec3 normal = cofactor * n * (dot(m[0], cofactor[0]) < 0.0 ? -1.0 : 1.0);

            vWorldPos = world.xyz;
            vNormal = dot(normal, normal) > 1e-20 ? normalize(normal) : vec3(0.0, 1.0, 0.0);
            vColor = aColor;
            gl_Position = uProjection * uView * world;
        }

        """;

    private const string ModelVertexBody = """

        uniform mat4 uModel;
        uniform int uAnimation;
        uniform float uPhase;

        void main()
        {
            vmPlace(uModel, uAnimation, uPhase);
        }

        """;

    private const string InstancedVertexBody = """

        // The model matrix's three columns, with the translation in their w components; then the tint; then emissive,
        // animation flags, phase and desaturation.
        layout(location = 3) in vec4 aModel0;
        layout(location = 4) in vec4 aModel1;
        layout(location = 5) in vec4 aModel2;
        layout(location = 6) in vec4 aTint;
        layout(location = 7) in vec4 aParams;

        out vec4 vTint;
        out vec2 vGlow;
        flat out int vAnimation;

        void main()
        {
            mat4 model = mat4(
                vec4(aModel0.xyz, 0.0),
                vec4(aModel1.xyz, 0.0),
                vec4(aModel2.xyz, 0.0),
                vec4(aModel0.w, aModel1.w, aModel2.w, 1.0));
            int animation = int(aParams.y + 0.5);
            vmPlace(model, animation, aParams.z);
            vTint = aTint;
            vGlow = vec2(aParams.x, aParams.w);
            vAnimation = animation;
        }

        """;

    /// <summary>Lights a model fragment, for both model programs.</summary>
    private const string ModelShading = """

        in vec3 vWorldPos;
        in vec3 vNormal;
        in vec3 vColor;

        uniform vec3 uSunDirection;
        uniform vec3 uSunColor;
        uniform vec3 uAmbientColor;
        uniform float uSpecularStrength;
        uniform float uShininess;

        out vec4 fragColor;

        vec4 vmShade(vec4 tint, float emissive, float desaturation, int animation)
        {
            vec3 base = vColor * tint.rgb;
            float luma = dot(base, vec3(0.299, 0.587, 0.114));
            base = mix(base, vec3(luma * 0.9 + 0.08), clamp(desaturation, 0.0, 1.0));
            if ((animation & 16) != 0) return vec4(vmApplyFog(base, vWorldPos), tint.a);

            vec3 N = normalize(vNormal);
            if (!gl_FrontFacing) N = -N;
            vec3 L = normalize(uSunDirection);
            vec3 V = normalize(uCameraPos - vWorldPos);
            vec3 H = normalize(L + V);

            float lambert = dot(N, L);
            float diffuse = max(lambert * 0.8 + 0.2, 0.0); // slight wrap keeps shaded faces readable
            float specular = lambert > 0.0 ? pow(max(dot(N, H), 0.0), uShininess) * uSpecularStrength : 0.0;
            float hemisphere = 0.8 + 0.35 * N.y;
            vec3 color = base * (uAmbientColor * hemisphere + uSunColor * diffuse) + uSunColor * specular;

            float glow = emissive;
            if ((animation & 4) != 0) glow *= 0.55 + 0.45 * sin(uTime * 5.0);
            color = mix(color, base * 1.2 + vec3(0.15), clamp(glow, 0.0, 1.0));

            return vec4(vmApplyFog(color, vWorldPos), tint.a);
        }

        """;

    private const string ModelFragmentBody = """

        uniform vec4 uTint;
        uniform float uEmissive;
        uniform float uDesaturation;
        uniform int uAnimation;

        void main()
        {
            fragColor = vmShade(uTint, uEmissive, uDesaturation, uAnimation);
        }

        """;

    private const string InstancedFragmentBody = """

        in vec4 vTint;
        in vec2 vGlow;
        flat in int vAnimation;

        void main()
        {
            fragColor = vmShade(vTint, vGlow.x, vGlow.y, vAnimation);
        }

        """;

    private const string WaterVertexBody = """

        layout(location = 0) in vec3 aPosition;

        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform vec2 uWaterCenter;
        uniform float uDetailRadius;

        out vec3 vWorldPos;
        out float vDetail;

        void main()
        {
            // Waves are only worked out near the marina. The sea beyond is a flat skirt of a few big triangles that
            // runs to the horizon, so the water never ends without costing anything to draw.
            float detail = 1.0 - smoothstep(uDetailRadius * 0.7, uDetailRadius, distance(aPosition.xz, uWaterCenter));
            vec2 grad;
            float h = vmWaveHeight(aPosition.xz, grad) * detail;
            vec3 world = vec3(aPosition.x, h, aPosition.z);
            vWorldPos = world;
            vDetail = detail;
            gl_Position = uProjection * uView * vec4(world, 1.0);
        }

        """;

    private const string ImageVertexBody = """

        layout(location = 0) in vec2 aCorner;

        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform vec2 uImageMin;
        uniform vec2 uImageMax;
        uniform float uImageHeight;

        out vec2 vUv;

        void main()
        {
            vec2 plan = mix(uImageMin, uImageMax, aCorner);
            vUv = aCorner;
            gl_Position = uProjection * uView * vec4(plan.x, uImageHeight, plan.y, 1.0);
        }

        """;

    private const string ImageFragmentBody = """

        in vec2 vUv;

        uniform sampler2D uImage;
        uniform float uOpacity;

        out vec4 fragColor;

        void main()
        {
            vec4 texel = texture(uImage, vUv);
            fragColor = vec4(texel.rgb, texel.a * uOpacity);
        }

        """;

    private const string WaterFragmentBody = """

        in vec3 vWorldPos;

        uniform vec3 uSunDirection;
        uniform vec3 uSunColor;
        uniform vec3 uAmbientColor;
        uniform vec3 uSkyColor;
        uniform vec3 uWaterDeep;
        uniform vec3 uWaterShallow;
        uniform float uSkyReflection;
        uniform float uRipples;
        uniform float uSunGlints;

        in float vDetail;

        out vec4 fragColor;

        void main()
        {
            vec2 grad;
            vmWaveHeight(vWorldPos.xz, grad);
            grad *= vDetail;

            // Fine procedural ripples that only perturb the normal. They fade out with distance
            // (and when a pixel covers several ripples) to avoid shimmering/moire.
            vec2 p = vWorldPos.xz;
            float t = uTime * uWaveSpeed;
            float viewDistance = length(uCameraPos - vWorldPos);
            float footprint = length(fwidth(p));
            float detail = clamp(1.0 - viewDistance / 260.0, 0.0, 1.0) * clamp(1.5 - footprint * 1.2, 0.0, 1.0) * uRipples * vDetail;
            grad += vec2(cos(p.x * 0.9 + p.y * 0.3 + t * 1.6), cos(p.y * 1.1 - p.x * 0.4 + t * 1.3)) * (0.06 * detail);
            grad += vec2(cos((p.x + p.y) * 1.7 - t * 2.2), cos((p.x - p.y) * 1.5 + t * 1.9)) * (0.035 * detail * detail);

            vec3 N = normalize(vec3(-grad.x, 1.0, -grad.y));
            vec3 L = normalize(uSunDirection);
            vec3 V = normalize(uCameraPos - vWorldPos);

            float fresnel = 0.04 + 0.96 * pow(1.0 - max(dot(N, V), 0.0), 5.0);
            float diffuse = max(dot(N, L), 0.0);
            vec3 body = mix(uWaterDeep, uWaterShallow, 0.3 + 0.4 * diffuse) * (uAmbientColor + uSunColor * 0.6);
            // Out on the skirt the sky reflection goes too: it is a plain body colour that the fog carries into
            // the horizon, which is the whole point of drawing it.
            vec3 color = mix(body, uSkyColor, clamp(fresnel, 0.0, 0.85) * uSkyReflection * vDetail);
            float sparkle = pow(max(dot(reflect(-L, N), V), 0.0), 90.0) * (0.35 + 0.65 * clamp(detail, 0.0, 1.0)) * uSunGlints * vDetail;
            color += uSunColor * sparkle;

            fragColor = vec4(vmApplyFog(color, vWorldPos), 1.0);
        }

        """;
}
