using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Rendering;

namespace VirtualMarina.Core.Geometry;

/// <summary>
/// World-space meshes for the ground: solid slabs for quays and lawns, rock piles for breakwaters, and the mainland
/// behind a <see cref="Shoreline"/>.
/// </summary>
public static class LandMeshFactory
{
    /// <summary>How far land walls reach below the water surface, in meters.</summary>
    public const float WallDepth = 3f;

    /// <summary>The mesh for a land area: a rock pile for <see cref="LandKind.Breakwater"/>, otherwise a solid slab; plus its trees.</summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.ForLand"/>).</param>
    /// <param name="area">The land area. Vertices are in world space, so the mesh is drawn with an identity transform.</param>
    /// <param name="style">Colors and tree visibility; the defaults when null.</param>
    /// <remarks>
    /// A visualizer does not draw land this way: it draws the ground alone and puts the rocks and trees on it as instances
    /// of a few shared meshes, which is far less to build and upload. This bakes the same instances into one mesh.
    /// </remarks>
    public static MeshData Create(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        AddGroundBase(b, area, style);
        Bake(b, CreateSceneryInstances(area, style));
        return b.Build(id, $"{(area.Kind == LandKind.Breakwater ? "Breakwater" : "Land")}:{area.Id}");
    }

    /// <summary>
    /// The ground of a land area on its own, without its trees: a rock pile for <see cref="LandKind.Breakwater"/>,
    /// otherwise a solid slab.
    /// </summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.ForLand"/>).</param>
    /// <param name="area">The land area. Vertices are in world space.</param>
    /// <param name="style">Colors; the defaults when null.</param>
    public static MeshData CreateGround(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        AddGroundBase(b, area, style);
        if (area.Kind == LandKind.Breakwater) Bake(b, CreateRockInstances(area, style));
        return b.Build(id, $"{(area.Kind == LandKind.Breakwater ? "Breakwater" : "Land")}:{area.Id}");
    }

    /// <summary>
    /// Just the trees of a land area, as a mesh of their own so they can be drawn over the ground and squashed onto
    /// it for a shadow. Empty when the style hides them or the area has none.
    /// </summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.ForLandTrees"/>).</param>
    /// <param name="area">The land area whose trees to build.</param>
    /// <param name="style">Colors and tree visibility; the defaults when null.</param>
    public static MeshData CreateTrees(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        if (style.ShowTrees)
        {
            var trees = new List<RenderObject>();
            AddTreeInstances(trees, area.Trees, area.Height, style);
            Bake(b, trees);
        }

        return b.Build(id, $"Trees:{area.Id}");
    }

    /// <summary>The outline extruded from <see cref="WallDepth"/> below the water up to the land height, with a flat top.</summary>
    public static MeshData CreateSlab(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        var b = new MeshBuilder();
        AddSlab(b, area, style ?? new LandStyle());
        return b.Build(id, $"Land:{area.Id}");
    }

    /// <summary>
    /// What a visualizer draws as a land area's own mesh: the slab, or for a breakwater the dark core its rocks stand on.
    /// The rocks and trees come from <see cref="CreateSceneryInstances"/>.
    /// </summary>
    internal static MeshData CreateGroundBase(int id, LandArea area, LandStyle style)
    {
        var b = new MeshBuilder();
        AddGroundBase(b, area, style);
        return b.Build(id, $"{(area.Kind == LandKind.Breakwater ? "Breakwater" : "Land")}:{area.Id}");
    }

    /// <summary>What stands on a land area, as instances of the shared scenery meshes: a breakwater's rocks and the trees.</summary>
    internal static RenderObject[] CreateSceneryInstances(LandArea area, LandStyle style)
    {
        var output = new List<RenderObject>();
        if (area.Kind == LandKind.Breakwater) output.AddRange(CreateRockInstances(area, style));
        if (style.ShowTrees) AddTreeInstances(output, area.Trees, area.Height, style);
        return output.ToArray();
    }

    private static void AddGroundBase(MeshBuilder b, LandArea area, LandStyle style)
    {
        if (area.Kind == LandKind.Breakwater)
        {
            // Core, kept below the rocks so it only shows through the gaps.
            var core = style.RockColor.ToVector3() * 0.5f;
            AddPrism(b, area.Points, -WallDepth, MathF.Max(-0.2f, RockPileEdgeHeight(area) - 0.35f), core, core);
        }
        else
        {
            AddSlab(b, area, style);
        }
    }

    private static void AddSlab(MeshBuilder b, LandArea area, LandStyle style)
    {
        var (top, wall) = area.Kind == LandKind.Grass ? (style.GrassColor, style.GrassBankColor) : (style.QuayColor, style.QuayWallColor);
        AddPrism(b, area.Points, -WallDepth, area.Height, top.ToVector3(), wall.ToVector3());
    }

    /// <summary>
    /// A rubble mound: the area filled with irregular rocks (low-poly squashed spheres), reaching the land height in the
    /// middle and sloping down to the water along the outline, over a dark core that hides the gaps between rocks.
    /// </summary>
    public static MeshData CreateRockPile(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        var core = style.RockColor.ToVector3() * 0.5f;
        AddPrism(b, area.Points, -WallDepth, MathF.Max(-0.2f, RockPileEdgeHeight(area) - 0.35f), core, core);
        Bake(b, CreateRockInstances(area, style));
        return b.Build(id, $"Breakwater:{area.Id}");
    }

    /// <summary>Height of a rock pile along its outline, where it meets the water.</summary>
    private static float RockPileEdgeHeight(LandArea area) => MathF.Max(0.1f, MathF.Max(area.Height, 0.3f) * 0.3f);

    /// <summary>The rocks of a breakwater, each one of the shared rock meshes stretched, turned and tinted.</summary>
    private static List<RenderObject> CreateRockInstances(LandArea area, LandStyle style)
    {
        var output = new List<RenderObject>();
        var points = area.Points;
        var rock = style.RockColor.ToVector3();
        var rockDark = rock * (1f - style.RockColorVariation);
        var rockLight = rock * (1f + style.RockColorVariation);
        var random = new Random((int)(MarinaMath.StableHash01(area.Id) * int.MaxValue));

        var height = MathF.Max(area.Height, 0.3f);
        var spacing = Math.Clamp(height * 0.8f, 1.4f, 2.4f);
        var edgeHeight = RockPileEdgeHeight(area);

        // Deepest point inside the outline sets how far the slope runs in.
        var (min, max) = PolygonMath.GetBounds(points);
        var samples = new List<(Vector2 Position, float Inset, bool Lower)>();
        for (var layer = 0; layer < 2; layer++)
        {
            var offset = layer * spacing * 0.5f;
            for (var y = min.Y + spacing * 0.5f + offset; y < max.Y; y += spacing)
            {
                for (var x = min.X + spacing * 0.5f + offset; x < max.X; x += spacing)
                {
                    var p = new Vector2(x, y) + Jitter(random, spacing * 0.3f);
                    if (!PolygonMath.Contains(points, p)) continue;
                    samples.Add((p, PolygonMath.DistanceToBoundary(points, p), layer == 1));
                }
            }
        }

        var deepest = samples.Count > 0 ? samples.Max(s => s.Inset) : 0f;
        var slope = MathF.Max(0.5f, MathF.Min(3f, deepest));

        foreach (var (position, inset, lower) in samples)
        {
            var t = SmoothStep(MathF.Min(inset / slope, 1f));
            var top = edgeHeight + (height - edgeHeight) * t;
            var radius = spacing * Lerp(0.5f, 0.68f, (float)random.NextDouble());
            // The second (offset) layer sits a little lower, filling the gaps of the first.
            output.Add(Rock(random, MarinaMath.ToWorld(position, top - radius * (lower ? 0.9f : 0.55f)), radius, rockDark, rockLight));
        }

        // A ring of rocks along the outline, at the waterline, covering the core's walls.
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var c = points[(i + 1) % points.Count];
            var length = Vector2.Distance(a, c);
            var count = Math.Max(1, (int)MathF.Ceiling(length / (spacing * 0.85f)));
            for (var k = 0; k < count; k++)
            {
                var along = Vector2.Lerp(a, c, (k + (float)random.NextDouble() * 0.5f) / count);
                var radius = spacing * Lerp(0.5f, 0.7f, (float)random.NextDouble());
                output.Add(Rock(random, MarinaMath.ToWorld(along, edgeHeight - radius * 0.6f), radius, rockDark, rockLight));
            }
        }

        return output;
    }

    /// <summary>One rock: a shared rock mesh squashed, stretched, turned and tinted somewhere between dark and light.</summary>
    private static RenderObject Rock(Random random, Vector3 center, float radius, Vector3 dark, Vector3 light)
    {
        var radii = new Vector3(
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()),
            radius * Lerp(0.6f, 0.85f, (float)random.NextDouble()),
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()));
        var yaw = (float)random.NextDouble() * MathF.Tau;
        var color = Vector3.Lerp(dark, light, (float)random.NextDouble()) *
            new Vector3(1f, 1f, Lerp(0.94f, 1.02f, (float)random.NextDouble()));
        var variant = random.Next(MeshIds.RockVariants);
        return new RenderObject(
            MeshIds.Rock(variant),
            Matrix4x4.CreateScale(radii) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(center),
            new Vector4(color, 1f));
    }

    /// <summary>
    /// Trunks and crowns of trees standing on flat ground, as instances of the shared tree meshes (one trunk, a few crowns
    /// per <see cref="TreeShape"/>). Colors vary slightly per tree (from its position).
    /// </summary>
    internal static void AddTreeInstances(List<RenderObject> output, IEnumerable<LandTree> trees, float groundHeight, LandStyle style)
    {
        var trunkColor = new Vector4(style.TrunkColor.ToVector3(), 1f);
        foreach (var tree in trees)
        {
            var hash = MarinaMath.StableHash01(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{tree.Position.X:0.00},{tree.Position.Y:0.00}"));
            var shade = 0.85f + hash * 0.3f;
            var ground = MarinaMath.ToWorld(tree.Position, groundHeight);
            var trunkHeight = tree.Shape switch
            {
                TreeShape.Conifer or TreeShape.Cypress => tree.Height * 0.25f,
                TreeShape.Palm => tree.Height * 0.78f,   // a bare trunk with the fronds right at the top
                _ => tree.Height * 0.4f,
            };

            // The trunk: from a little below the ground to just inside the crown, tapering to 70 %.
            var trunkRadius = MathF.Max(0.1f, tree.CrownRadius * (tree.Shape == TreeShape.Palm ? 0.08f : 0.12f));
            var trunkLength = trunkHeight + tree.CrownRadius * 0.3f + 0.2f;
            output.Add(new RenderObject(
                MeshIds.TreeTrunk,
                Matrix4x4.CreateScale(trunkRadius, trunkLength, trunkRadius) * Matrix4x4.CreateTranslation(ground - Vector3.UnitY * 0.2f),
                trunkColor));

            // Which of the shape's crowns, and which way it faces: fixed per tree, so the forest does not reshuffle.
            var variant = (int)(hash * 977f) % MeshIds.TreeVariants;
            var yaw = hash * 131f % 1f * MathF.Tau;
            var r = tree.CrownRadius;
            var (crown, color) = tree.Shape switch
            {
                // One tall narrow cone: the Mediterranean exclamation mark.
                TreeShape.Cypress => (
                    Matrix4x4.CreateScale(r, tree.Height - trunkHeight, r) * Matrix4x4.CreateTranslation(ground + Vector3.UnitY * trunkHeight),
                    style.ConiferColor.ToVector3() * shade * 0.95f),

                // Two stacked cones.
                TreeShape.Conifer => (
                    Matrix4x4.CreateScale(r, tree.Height - trunkHeight, r) * Matrix4x4.CreateTranslation(ground + Vector3.UnitY * trunkHeight),
                    style.ConiferColor.ToVector3() * shade),

                // A spray of fronds leaning out from the top of the trunk.
                TreeShape.Palm => (
                    Matrix4x4.CreateScale(r) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(ground + Vector3.UnitY * trunkHeight),
                    style.PalmColor.ToVector3() * shade),

                // Blossom rather than leaves, and a wider, lower crown.
                TreeShape.Cherry => (
                    Matrix4x4.CreateScale(r) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(ground + Vector3.UnitY * (tree.Height - r * 0.8f)),
                    style.BlossomColor.ToVector3() * (0.92f + hash * 0.16f)),

                _ => (
                    Matrix4x4.CreateScale(r) * Matrix4x4.CreateRotationY(yaw) * Matrix4x4.CreateTranslation(ground + Vector3.UnitY * (tree.Height - r)),
                    style.FoliageColor.ToVector3() * shade),
            };

            output.Add(new RenderObject(MeshIds.TreeCrown(tree.Shape, variant), crown, new Vector4(color, 1f)));
        }
    }

    /// <summary>
    /// The meshes scenery is drawn with: one trunk, <see cref="MeshIds.TreeVariants"/> crowns per <see cref="TreeShape"/>
    /// and <see cref="MeshIds.RockVariants"/> rocks. White or near-white, sized to one meter, tinted per instance.
    /// </summary>
    internal static IEnumerable<MeshData> CreateSceneryMeshes()
    {
        var white = Vector3.One;
        var trunk = new MeshBuilder();
        trunk.AddCylinder(Vector3.Zero, Vector3.UnitY, 1f, 0.7f, 6, white);
        yield return trunk.Build(MeshIds.TreeTrunk, "TreeTrunk");

        foreach (var shape in Enum.GetValues<TreeShape>())
        {
            for (var variant = 0; variant < MeshIds.TreeVariants; variant++)
            {
                var random = new Random(7919 * ((int)shape + 1) + variant);
                var b = new MeshBuilder();
                switch (shape)
                {
                    case TreeShape.Cypress:
                        // Scaled by the crown radius across and the crown height up.
                        b.AddCylinder(Vector3.Zero, Vector3.UnitY, 1f, 0.15f, 7, white);
                        break;

                    case TreeShape.Conifer:
                        b.AddCylinder(Vector3.Zero, Vector3.UnitY * 0.65f, 1f, 0.35f, 7, white);
                        b.AddCylinder(Vector3.UnitY * 0.4f, Vector3.UnitY, 0.75f, 0f, 7, white * 1.05f);
                        break;

                    case TreeShape.Palm:
                        // Crown radius 1, fronds leaning out and down from the top of the trunk.
                        for (var frond = 0; frond < 6; frond++)
                        {
                            var angle = (frond + (float)random.NextDouble() * 0.4f) / 6f * MathF.Tau;
                            var reach = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * 0.85f;
                            b.AddCylinder(Vector3.Zero, reach - Vector3.UnitY * 0.3f, 0.16f, 0f, 5, white);
                        }

                        break;

                    case TreeShape.Cherry:
                        {
                            AddRock(b, random, Vector3.Zero, 1f, white * 0.95f, white, flatten: false);
                            var offset = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 1.1f;
                            AddRock(b, random, offset - Vector3.UnitY * 0.2f, 0.72f, white, white * 1.06f, flatten: false);
                            break;
                        }

                    default:
                        {
                            AddRock(b, random, Vector3.Zero, 1f, white, white * 1.12f, flatten: false);
                            var offset = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f);
                            AddRock(b, random, offset - Vector3.UnitY * 0.25f, 0.7f, white * 0.9f, white, flatten: false);
                            break;
                        }
                }

                yield return b.Build(MeshIds.TreeCrown(shape, variant), $"TreeCrown:{shape}:{variant}");
            }
        }

        for (var variant = 0; variant < MeshIds.RockVariants; variant++)
        {
            // Round and white: each rock instance gives it its proportions, its turn and its color.
            var b = new MeshBuilder();
            AddRockShape(b, new Random(104_729 + variant), Vector3.Zero, Vector3.One, 0f, white);
            yield return b.Build(MeshIds.Rock(variant), $"Rock:{variant}");
        }
    }

    /// <summary>The scenery meshes by id, for baking instances into one mesh.</summary>
    private static readonly Lazy<Dictionary<int, MeshData>> BakeMeshes = new(() =>
    {
        var meshes = CreateSceneryMeshes().ToDictionary(mesh => mesh.Id);
        meshes[MeshIds.UnitBox] = MarinaMeshFactory.CreateUnitBox(MeshIds.UnitBox);
        meshes[MeshIds.BerthPad] = MarinaMeshFactory.CreateBerthPad(MeshIds.BerthPad);
        return meshes;
    });

    /// <summary>Adds instances of the scenery meshes to a mesh, transformed and tinted.</summary>
    private static void Bake(MeshBuilder b, IEnumerable<RenderObject> instances)
    {
        foreach (var instance in instances)
        {
            if (BakeMeshes.Value.TryGetValue(instance.MeshId, out var mesh)) b.AddTransformed(mesh, instance.World, new Vector3(instance.Tint.X, instance.Tint.Y, instance.Tint.Z));
        }
    }

    /// <summary>How far the mainland sits below a land area of the same height, so a quay traced along the shore always wins.</summary>
    private const float Underlap = 0.05f;

    /// <summary>
    /// How far inland the scenery reaches, in meters. Beyond it the mainland is bare ground running to the horizon.
    /// Kept shallow on purpose: a band close to the water reads as a wooded or built-up shore, where the same things
    /// spread thinly over a mile of hinterland only read as litter.
    /// </summary>
    private const float SceneryDepth = 260f;

    /// <summary>How far past the ends of the drawn line the scenery carries on, in meters.</summary>
    private const float SceneryRun = 800f;

    /// <summary>
    /// The mainland behind the marina: the shoreline's shape as one slab, with whatever scenery it asks for scattered
    /// in a band along the coast.
    /// </summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.Shoreline"/>).</param>
    /// <param name="shoreline">The shoreline. Vertices are in world space, so the mesh is drawn with an identity transform.</param>
    /// <param name="style">Colors and tree visibility; the defaults when null.</param>
    /// <remarks>
    /// The ground itself is a handful of triangles however far it reaches, and the scenery is capped and thins out
    /// inland, so a long coast costs no more to draw than a short one. An empty mesh comes back when the shoreline is
    /// one <see cref="Shoreline.Validate"/> refuses.
    /// </remarks>
    public static MeshData CreateShoreline(int id, Shoreline shoreline, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(shoreline);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        var outline = shoreline.BuildOutline();
        if (outline.Count < 3) return b.Build(id, "Shoreline");

        AddShorelineGround(b, shoreline, outline, style);
        Bake(b, CreateShorelineSceneryInstances(shoreline, outline, style));
        return b.Build(id, "Shoreline");
    }

    /// <summary>The mainland's ground on its own, without whatever is scattered over it.</summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.Shoreline"/>).</param>
    /// <param name="shoreline">The shoreline.</param>
    /// <param name="style">Colors; the defaults when null.</param>
    public static MeshData CreateShorelineGround(int id, Shoreline shoreline, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(shoreline);
        var b = new MeshBuilder();
        var outline = shoreline.BuildOutline();
        if (outline.Count >= 3) AddShorelineGround(b, shoreline, outline, style ?? new LandStyle());
        return b.Build(id, "Shoreline");
    }

    /// <summary>
    /// Just what stands on the mainland — trees, crops or a town — as a mesh of its own, so it can cast a shadow on
    /// the ground it stands on.
    /// </summary>
    /// <param name="id">Mesh id (see <see cref="MeshIds.ShorelineScenery"/>).</param>
    /// <param name="shoreline">The shoreline whose scenery to build.</param>
    /// <param name="style">Colors and tree visibility; the defaults when null.</param>
    public static MeshData CreateShorelineScenery(int id, Shoreline shoreline, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(shoreline);
        var b = new MeshBuilder();
        var outline = shoreline.BuildOutline();
        if (outline.Count >= 3) Bake(b, CreateShorelineSceneryInstances(shoreline, outline, style ?? new LandStyle()));
        return b.Build(id, "ShorelineScenery");
    }

    /// <summary>World Y of the mainland's surface: a hair under the land areas, so a quay on the shore wins.</summary>
    public static float ShorelineGroundHeight(Shoreline shoreline)
    {
        ArgumentNullException.ThrowIfNull(shoreline);
        return shoreline.Height - Underlap;
    }

    private static void AddShorelineGround(MeshBuilder b, Shoreline shoreline, IReadOnlyList<Vector2> outline, LandStyle style)
    {
        var (top, wall) = shoreline.Kind == LandKind.Grass ? (style.GrassColor, style.GrassBankColor) : (style.QuayColor, style.QuayWallColor);
        AddPrism(b, outline, -WallDepth, ShorelineGroundHeight(shoreline), top.ToVector3(), wall.ToVector3());
    }

    /// <summary>
    /// Whatever stands on the mainland — trees, crops or a town, in a band along the coast — as instances of the shared
    /// scenery meshes, standing on <see cref="ShorelineGroundHeight"/>. Empty when the shoreline has no outline.
    /// </summary>
    internal static RenderObject[] CreateShorelineSceneryInstances(Shoreline shoreline, LandStyle style)
    {
        var outline = shoreline.BuildOutline();
        return outline.Count >= 3 ? CreateShorelineSceneryInstances(shoreline, outline, style) : [];
    }

    private static RenderObject[] CreateShorelineSceneryInstances(Shoreline shoreline, IReadOnlyList<Vector2> outline, LandStyle style)
    {
        var output = new List<RenderObject>();
        var ground = ShorelineGroundHeight(shoreline);
        var random = new Random(shoreline.ScenerySeed);
        switch (shoreline.Scenery)
        {
            case HinterlandScenery.Countryside:
                if (style.ShowTrees) AddHinterlandTrees(output, shoreline, outline, style, random, ground, new TreeBand(340, 10f, SceneryDepth));
                break;

            case HinterlandScenery.Fields:
                AddFields(output, shoreline, outline, style, random, ground);
                if (style.ShowTrees) AddHinterlandTrees(output, shoreline, outline, style, random, ground, new TreeBand(60, 12f, SceneryDepth));
                break;

            case HinterlandScenery.Town:
                AddTown(output, shoreline, outline, style, random, ground);
                if (style.ShowTrees) AddHinterlandTrees(output, shoreline, outline, style, random, ground, new TreeBand(70, 18f, SceneryDepth * 0.8f));
                break;
        }

        return output.ToArray();
    }

    /// <summary>How many hinterland trees to try, and the band inland they stand in: <see cref="Near"/> is how far inland it starts, <see cref="Depth"/> how deep it is.</summary>
    private readonly record struct TreeBand(int Count, float Near, float Depth);

    /// <summary>Trees of the same mix as a land area's, standing on the mainland rather than inside an outline.</summary>
    private static void AddHinterlandTrees(
        List<RenderObject> output, Shoreline shoreline, IReadOnlyList<Vector2> outline, LandStyle style, Random random, float ground, TreeBand band)
    {
        var trees = new List<LandTree>(band.Count);

        // The trees placed so far, bucketed by position, so keeping new ones clear of them looks at a few neighbours
        // rather than at every tree.
        const float cell = 8f;
        var grid = new Dictionary<(int, int), List<LandTree>>();
        var largest = 0f;
        foreach (var (position, _, _) in ScatterInland(shoreline, outline, random, band.Count, band.Near, band.Depth))
        {
            var shape = LandArea.PickShape(random);
            var height = shape switch
            {
                TreeShape.Conifer => 6f + (float)random.NextDouble() * 7f,
                TreeShape.Cypress => 7f + (float)random.NextDouble() * 5f,
                TreeShape.Palm => 5f + (float)random.NextDouble() * 5f,
                TreeShape.Cherry => 4f + (float)random.NextDouble() * 3f,
                _ => 4f + (float)random.NextDouble() * 5f,
            };

            var radius = shape switch
            {
                TreeShape.Cypress => height * 0.09f,
                TreeShape.Palm => height * 0.24f,
                TreeShape.Conifer => height * 0.18f,
                _ => height * 0.34f,
            };

            // Far enough apart to read as separate trees.
            var reach = (int)MathF.Ceiling((largest + radius) * 0.9f / cell);
            var (cx, cy) = ((int)MathF.Floor(position.X / cell), (int)MathF.Floor(position.Y / cell));
            if (TooClose(grid, cx, cy, reach, position, radius)) continue;

            var tree = new LandTree(position, height, radius, shape);
            trees.Add(tree);
            if (!grid.TryGetValue((cx, cy), out var bucket)) grid[(cx, cy)] = bucket = [];
            bucket.Add(tree);
            largest = MathF.Max(largest, radius);
        }

        AddTreeInstances(output, trees, ground, style);
    }

    /// <summary>True when a tree of <paramref name="radius"/> at <paramref name="position"/> would crowd one already placed.</summary>
    private static bool TooClose(Dictionary<(int, int), List<LandTree>> grid, int cx, int cy, int reach, Vector2 position, float radius)
    {
        for (var x = cx - reach; x <= cx + reach; x++)
        {
            for (var y = cy - reach; y <= cy + reach; y++)
            {
                if (grid.TryGetValue((x, y), out var bucket) &&
                    bucket.Exists(t => Vector2.Distance(t.Position, position) < (t.CrownRadius + radius) * 0.9f))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Crops seen from the air: flat blocks of colour lying on the ground, lined up with the coast.</summary>
    private static void AddFields(List<RenderObject> output, Shoreline shoreline, IReadOnlyList<Vector2> outline, LandStyle style, Random random, float ground)
    {
        var grass = style.GrassColor.ToVector3();
        foreach (var (position, _, along) in ScatterInland(shoreline, outline, random, 80, 20f, SceneryDepth))
        {
            var across = new Vector2(-along.Y, along.X);
            var half = new Vector2(30f + (float)random.NextDouble() * 50f, 22f + (float)random.NextDouble() * 34f);
            // Crops run from dark green through to bare, dusty earth.
            var ripeness = (float)random.NextDouble();
            var color = Vector3.Lerp(grass * 0.82f, new Vector3(0.78f, 0.70f, 0.42f), ripeness * ripeness);

            // The unit pad stretched along the coast and inland, a hair above the ground so the field shows rather
            // than fighting the surface for the same pixels.
            var level = ground + 0.03f;
            var world = new Matrix4x4(
                along.X * half.X * 2f, 0f, along.Y * half.X * 2f, 0f,
                0f, 1f, 0f, 0f,
                across.X * half.Y * 2f, 0f, across.Y * half.Y * 2f, 0f,
                position.X, level, position.Y, 1f);
            output.Add(new RenderObject(MeshIds.BerthPad, world, new Vector4(color, 1f)));
        }
    }

    /// <summary>A town: plain blocks with roofs, standing thickest and tallest near the water.</summary>
    private static void AddTown(List<RenderObject> output, Shoreline shoreline, IReadOnlyList<Vector2> outline, LandStyle style, Random random, float ground)
    {
        const float depth = SceneryDepth * 0.7f;
        var walls = style.BuildingColor.ToVector3();
        var roofs = style.RoofColor.ToVector3();
        var placed = new List<(Vector2 Center, float Radius)>(140);

        foreach (var (position, inland, _) in ScatterInland(shoreline, outline, random, 140, 25f, depth))
        {
            var seafront = 1f - Math.Clamp(inland / depth, 0f, 1f);
            var footprint = new Vector2(9f + (float)random.NextDouble() * 9f, 9f + (float)random.NextDouble() * 9f);
            var radius = MathF.Max(footprint.X, footprint.Y) * 0.5f;
            if (placed.Exists(other => Vector2.Distance(other.Center, position) < other.Radius + radius + 6f)) continue;
            placed.Add((position, radius));

            var height = 5f + (float)random.NextDouble() * (5f + seafront * 14f);
            var shade = Lerp(0.86f, 1.12f, (float)random.NextDouble());
            output.Add(new RenderObject(
                MeshIds.UnitBox,
                Matrix4x4.CreateScale(footprint.X, height, footprint.Y) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, ground + height * 0.5f)),
                new Vector4(walls * shade, 1f)));
            output.Add(new RenderObject(
                MeshIds.UnitBox,
                Matrix4x4.CreateScale(footprint.X * 1.12f, 1.2f, footprint.Y * 1.12f) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, ground + height + 0.6f)),
                new Vector4(roofs * Lerp(0.9f, 1.1f, (float)random.NextDouble()), 1f)));
        }
    }

    /// <summary>
    /// Places scattered in a band of mainland along the coast, thickest at the water's edge and thinning inland.
    /// </summary>
    /// <remarks>
    /// The band follows the drawn line and carries on past both ends, so the scenery does not stop dead where the
    /// designer stopped clicking. Places that come out over water — inside a bay the line cuts back into — are
    /// dropped, which is what keeps the scenery on the land side without any extra work.
    /// </remarks>
    /// <param name="shoreline">The coast.</param>
    /// <param name="outline">The shoreline's built outline (<see cref="Shoreline.BuildOutline"/>), worked out once by the caller.</param>
    /// <param name="random">Where the places come from.</param>
    /// <param name="count">How many places at most.</param>
    /// <param name="near">How far inland the band starts.</param>
    /// <param name="depth">How deep the band is.</param>
    /// <returns>For each place: where it is, how far inland it fell, and the direction of the coast beside it.</returns>
    private static IEnumerable<(Vector2 Position, float Inland, Vector2 Along)> ScatterInland(
        Shoreline shoreline, IReadOnlyList<Vector2> outline, Random random, int count, float near, float depth)
    {
        var line = ExtendedLine(shoreline);
        var lengths = new float[line.Count - 1];
        var total = 0f;
        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = Vector2.Distance(line[i], line[i + 1]);
            total += lengths[i];
        }

        if (total < 1f) yield break;

        var placed = 0;
        for (var attempt = 0; attempt < count * 3 && placed < count; attempt++)
        {
            // Somewhere along the coast, by length rather than by point, so long stretches get their share.
            var along = (float)random.NextDouble() * total;
            var segment = 0;
            while (segment < lengths.Length - 1 && along > lengths[segment]) along -= lengths[segment++];

            var step = line[segment + 1] - line[segment];
            if (step.LengthSquared() < 1e-6f) continue;
            var direction = Vector2.Normalize(step);
            var inward = shoreline.LandOnLeft ? new Vector2(-direction.Y, direction.X) : new Vector2(direction.Y, -direction.X);

            // Squaring the step inland puts more of it near the water, where it is actually seen.
            var reach = (float)random.NextDouble();
            var inland = near + depth * reach * reach;
            var position = line[segment] + direction * along + inward * inland;
            if (!PolygonMath.Contains(outline, position)) continue;

            placed++;
            yield return (position, inland, direction);
        }
    }

    /// <summary>The drawn line with both ends carried on, so scenery does not end where the drawing did.</summary>
    private static List<Vector2> ExtendedLine(Shoreline shoreline)
    {
        var points = shoreline.Points;
        var line = new List<Vector2>(points.Count + 2) { points[0] + Onward(points[1], points[0]) * SceneryRun };
        line.AddRange(points);
        line.Add(points[^1] + Onward(points[^2], points[^1]) * SceneryRun);
        return line;
    }

    /// <summary>The unit direction from one point through the next, and on.</summary>
    private static Vector2 Onward(Vector2 from, Vector2 through)
    {
        var step = through - from;
        return step.LengthSquared() < 1e-8f ? Vector2.UnitX : Vector2.Normalize(step);
    }

    /// <summary>Flat top and vertical walls of a (possibly concave) outline.</summary>
    private static void AddPrism(MeshBuilder b, IReadOnlyList<Vector2> points, float bottom, float top, Vector3 topColor, Vector3 wallColor)
    {
        foreach (var (i0, i1, i2) in PolygonMath.Triangulate(points))
        {
            var a = MarinaMath.ToWorld(points[i0], top);
            var c = MarinaMath.ToWorld(points[i1], top);
            var d = MarinaMath.ToWorld(points[i2], top);
            b.AddTriangleFacingAway(a, c, d, topColor, (a + c + d) / 3f - Vector3.UnitY);
        }

        // Outward normal of an edge a→c: to the right of the edge for counter-clockwise (positive area) outlines.
        var sign = PolygonMath.SignedArea(points) >= 0f ? 1f : -1f;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var c = points[(i + 1) % points.Count];
            var edge = c - a;
            if (edge.LengthSquared() < 1e-8f) continue;
            var outward = Vector2.Normalize(new Vector2(edge.Y, -edge.X)) * sign;
            var inside = MarinaMath.ToWorld((a + c) * 0.5f - outward, (top + bottom) * 0.5f);

            var a0 = MarinaMath.ToWorld(a, bottom);
            var a1 = MarinaMath.ToWorld(a, top);
            var c0 = MarinaMath.ToWorld(c, bottom);
            var c1 = MarinaMath.ToWorld(c, top);
            b.AddFace(new[] { a0, c0, c1, a1 }, wallColor, inside);
        }
    }

    /// <summary>An irregular, flattened low-poly ball.</summary>
    private static void AddRock(MeshBuilder b, Random random, Vector3 center, float radius, Vector3 dark, Vector3 light, bool flatten = true)
    {
        var radii = new Vector3(
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()),
            radius * (flatten ? Lerp(0.6f, 0.85f, (float)random.NextDouble()) : Lerp(0.85f, 1.05f, (float)random.NextDouble())),
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()));
        var yaw = (float)random.NextDouble() * MathF.Tau;
        var color = Vector3.Lerp(dark, light, (float)random.NextDouble()) *
            new Vector3(1f, 1f, Lerp(0.94f, 1.02f, (float)random.NextDouble()));
        AddRockShape(b, random, center, radii, yaw, color);
    }

    /// <summary>A low-poly ball with these radii, its middle rings pushed in and out at random.</summary>
    private static void AddRockShape(MeshBuilder b, Random random, Vector3 center, Vector3 radii, float yaw, Vector3 color)
    {
        const int segments = 6;
        const int rings = 3;
        Vector3[]? previous = null;
        for (var r = 0; r <= rings; r++)
        {
            var latitude = -MathF.PI / 2f + MathF.PI * r / rings;
            var ring = new Vector3[segments];
            for (var s = 0; s < segments; s++)
            {
                var angle = yaw + MathF.Tau * s / segments;
                var bump = r == 0 || r == rings ? 1f : Lerp(0.82f, 1.12f, (float)random.NextDouble());
                ring[s] = center + new Vector3(
                    MathF.Cos(latitude) * MathF.Cos(angle) * radii.X * bump,
                    MathF.Sin(latitude) * radii.Y,
                    MathF.Cos(latitude) * MathF.Sin(angle) * radii.Z * bump);
            }

            if (previous is not null) b.AddLoft(previous, ring, color, null, null, center);
            previous = ring;
        }
    }

    private static Vector2 Jitter(Random random, float amount) =>
        new(((float)random.NextDouble() * 2f - 1f) * amount, ((float)random.NextDouble() * 2f - 1f) * amount);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}
