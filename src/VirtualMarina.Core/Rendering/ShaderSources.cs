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
/// Attributes: location 0 = position, 1 = normal, 2 = color.
/// Frame uniforms: uView, uProjection, uCameraPos, uTime, uSunDirection, uSunColor, uAmbientColor,
/// uSpecularStrength, uShininess, uSkyColor, uFogColor, uFogDensity, uWaterDeep, uWaterShallow,
/// uWaveAmplitude, uWaveFrequency, uWaveSpeed, uSkyReflection, uRipples, uSunGlints (water) and uFloatMotion (model).
/// Object uniforms: uModel, uTint, uEmissive, uDesaturation, uAnimation, uPhase.
/// </remarks>
public static class ShaderSources
{
    /// <summary>Vertex shader for all objects: placement plus GPU animations (floating, spin, above-waves lift).</summary>
    public static string ModelVertex(ShaderDialect dialect) => Header(dialect) + WaveFunctions + ModelVertexBody;

    /// <summary>Fragment shader for all objects: lighting, tint, emissive highlight, desaturation and fog.</summary>
    public static string ModelFragment(ShaderDialect dialect) => Header(dialect) + WaveFunctions + FogFunction + ModelFragmentBody;

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

    private const string ModelVertexBody = """

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform int uAnimation;
        uniform float uPhase;
        uniform float uFloatMotion;

        out vec3 vWorldPos;
        out vec3 vNormal;
        out vec3 vColor;

        mat3 vmRotX(float a) { float c = cos(a); float s = sin(a); return mat3(1.0, 0.0, 0.0, 0.0, c, s, 0.0, -s, c); }
        mat3 vmRotY(float a) { float c = cos(a); float s = sin(a); return mat3(c, 0.0, -s, 0.0, 1.0, 0.0, s, 0.0, c); }
        mat3 vmRotZ(float a) { float c = cos(a); float s = sin(a); return mat3(c, s, 0.0, -s, c, 0.0, 0.0, 0.0, 1.0); }

        void main()
        {
            vec3 p = aPosition;
            vec3 n = aNormal;
            float lift = 0.0;
            vec3 origin = uModel[3].xyz;

            if ((uAnimation & 1) != 0)
            {
                vec2 grad;
                float h = vmWaveHeight(origin.xz, grad);
                float t = uTime * uWaveSpeed;
                float roll = (sin(t * 0.8 + uPhase) * 0.02 + clamp(grad.x, -0.2, 0.2) * 0.3) * uFloatMotion;
                float pitch = (sin(t * 0.63 + uPhase * 1.7) * 0.012 + clamp(grad.y, -0.2, 0.2) * 0.2) * uFloatMotion;
                mat3 r = vmRotZ(roll) * vmRotX(pitch);
                p = r * p;
                n = r * n;
                lift += h * clamp(uFloatMotion, 0.0, 1.0);
            }

            if ((uAnimation & 8) != 0)
            {
                // Always above the highest possible wave, so water can never cover the object from a camera above it.
                lift += vmMaxWaveHeight();
            }

            if ((uAnimation & 2) != 0)
            {
                mat3 r = vmRotY(uTime * 1.5 + uPhase);
                p = r * p;
                n = r * n;
                lift += sin(uTime * 2.5 + uPhase) * 0.35;
            }

            vec4 world = uModel * vec4(p, 1.0);
            world.y += lift;
            mat3 normalMatrix = transpose(inverse(mat3(uModel)));

            vWorldPos = world.xyz;
            vNormal = normalize(normalMatrix * n);
            vColor = aColor;
            gl_Position = uProjection * uView * world;
        }

        """;

    private const string ModelFragmentBody = """

        in vec3 vWorldPos;
        in vec3 vNormal;
        in vec3 vColor;

        uniform vec4 uTint;
        uniform float uEmissive;
        uniform float uDesaturation;
        uniform int uAnimation;
        uniform vec3 uSunDirection;
        uniform vec3 uSunColor;
        uniform vec3 uAmbientColor;
        uniform float uSpecularStrength;
        uniform float uShininess;

        out vec4 fragColor;

        void main()
        {
            vec3 N = normalize(vNormal);
            if (!gl_FrontFacing) N = -N;
            vec3 L = normalize(uSunDirection);
            vec3 V = normalize(uCameraPos - vWorldPos);
            vec3 H = normalize(L + V);

            vec3 base = vColor * uTint.rgb;
            float luma = dot(base, vec3(0.299, 0.587, 0.114));
            base = mix(base, vec3(luma * 0.9 + 0.08), clamp(uDesaturation, 0.0, 1.0));
            float lambert = dot(N, L);
            float diffuse = max(lambert * 0.8 + 0.2, 0.0); // slight wrap keeps shaded faces readable
            float specular = lambert > 0.0 ? pow(max(dot(N, H), 0.0), uShininess) * uSpecularStrength : 0.0;
            float hemisphere = 0.8 + 0.35 * N.y;
            vec3 color = base * (uAmbientColor * hemisphere + uSunColor * diffuse) + uSunColor * specular;

            float glow = uEmissive;
            if ((uAnimation & 4) != 0) glow *= 0.55 + 0.45 * sin(uTime * 5.0);
            color = mix(color, base * 1.2 + vec3(0.15), clamp(glow, 0.0, 1.0));

            fragColor = vec4(vmApplyFog(color, vWorldPos), uTint.a);
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
