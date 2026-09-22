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
    public static MeshData Create(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        style ??= new LandStyle();
        var b = new MeshBuilder();
        if (area.Kind == LandKind.Breakwater) AddRockPile(b, area, style);
        else AddSlab(b, area, style);
        if (style.ShowTrees) AddTrees(b, area, style);
        return b.Build(id, $"{(area.Kind == LandKind.Breakwater ? "Breakwater" : "Land")}:{area.Id}");
    }

    /// <summary>The outline extruded from <see cref="WallDepth"/> below the water up to the land height, with a flat top.</summary>
    public static MeshData CreateSlab(int id, LandArea area, LandStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(area);
        var b = new MeshBuilder();
        AddSlab(b, area, style ?? new LandStyle());
        return b.Build(id, $"Land:{area.Id}");
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
        var b = new MeshBuilder();
        AddRockPile(b, area, style ?? new LandStyle());
        return b.Build(id, $"Breakwater:{area.Id}");
    }

    private static void AddRockPile(MeshBuilder b, LandArea area, LandStyle style)
    {
        var points = area.Points;
        var rock = style.RockColor.ToVector3();
        var rockDark = rock * (1f - style.RockColorVariation);
        var rockLight = rock * (1f + style.RockColorVariation);
        var core = rock * 0.5f;
        var random = new Random((int)(MarinaMath.StableHash01(area.Id) * int.MaxValue));

        var height = MathF.Max(area.Height, 0.3f);
        var spacing = Math.Clamp(height * 0.8f, 1.4f, 2.4f);
        var edgeHeight = MathF.Max(0.1f, height * 0.3f);

        // Core, kept below the rocks so it only shows through the gaps.
        AddPrism(b, points, -WallDepth, MathF.Max(-0.2f, edgeHeight - 0.35f), core, core);

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
            AddRock(b, random, MarinaMath.ToWorld(position, top - radius * (lower ? 0.9f : 0.55f)), radius, rockDark, rockLight);
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
                AddRock(b, random, MarinaMath.ToWorld(along, edgeHeight - radius * 0.6f), radius, rockDark, rockLight);
            }
        }
    }

    /// <summary>Trunks and crowns of the area's trees, standing on its surface. Colors vary slightly per tree (from its position).</summary>
    private static void AddTrees(MeshBuilder b, LandArea area, LandStyle style) => AddTrees(b, area.Trees, area.Height, style);

    /// <summary>The same, for trees standing on any flat ground — the mainland behind the shore has no land area.</summary>
    private static void AddTrees(MeshBuilder b, IEnumerable<LandTree> trees, float groundHeight, LandStyle style)
    {
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

            var trunkRadius = MathF.Max(0.1f, tree.CrownRadius * (tree.Shape == TreeShape.Palm ? 0.08f : 0.12f));
            b.AddCylinder(ground - Vector3.UnitY * 0.2f, ground + Vector3.UnitY * (trunkHeight + tree.CrownRadius * 0.3f), trunkRadius, trunkRadius * 0.7f, 6, style.TrunkColor.ToVector3());

            if (tree.Shape == TreeShape.Cypress)
            {
                // One tall narrow cone: the Mediterranean exclamation mark.
                var color = style.ConiferColor.ToVector3() * shade * 0.95f;
                b.AddCylinder(ground + Vector3.UnitY * trunkHeight, ground + Vector3.UnitY * tree.Height, tree.CrownRadius, tree.CrownRadius * 0.15f, 7, color);
            }
            else if (tree.Shape == TreeShape.Palm)
            {
                // A spray of fronds: flattened blobs leaning out from the top of the trunk.
                var color = style.PalmColor.ToVector3() * shade;
                var crown = ground + Vector3.UnitY * trunkHeight;
                var random = new Random((int)(hash * int.MaxValue));
                for (var frond = 0; frond < 6; frond++)
                {
                    var angle = (frond + (float)random.NextDouble() * 0.4f) / 6f * MathF.Tau;
                    var reach = new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * tree.CrownRadius * 0.85f;
                    b.AddCylinder(crown, crown + reach - Vector3.UnitY * tree.CrownRadius * 0.3f, tree.CrownRadius * 0.16f, 0f, 5, color);
                }
            }
            else if (tree.Shape == TreeShape.Cherry)
            {
                // Blossom rather than leaves, and a wider, lower crown.
                var color = style.BlossomColor.ToVector3() * (0.92f + hash * 0.16f);
                var center = ground + Vector3.UnitY * (tree.Height - tree.CrownRadius * 0.8f);
                var random = new Random((int)(hash * int.MaxValue));
                AddRock(b, random, center, tree.CrownRadius, color * 0.95f, color, flatten: false);
                var offset = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * tree.CrownRadius * 1.1f;
                AddRock(b, random, center + offset - Vector3.UnitY * tree.CrownRadius * 0.2f, tree.CrownRadius * 0.72f, color, color * 1.06f, flatten: false);
            }
            else if (tree.Shape == TreeShape.Conifer)
            {
                var color = style.ConiferColor.ToVector3() * shade;
                var crownHeight = tree.Height - trunkHeight;
                // Two stacked cones.
                b.AddCylinder(ground + Vector3.UnitY * trunkHeight, ground + Vector3.UnitY * (trunkHeight + crownHeight * 0.65f), tree.CrownRadius, tree.CrownRadius * 0.35f, 7, color);
                b.AddCylinder(ground + Vector3.UnitY * (trunkHeight + crownHeight * 0.4f), ground + Vector3.UnitY * tree.Height, tree.CrownRadius * 0.75f, 0f, 7, color * 1.05f);
            }
            else
            {
                var color = style.FoliageColor.ToVector3() * shade;
                var center = ground + Vector3.UnitY * (tree.Height - tree.CrownRadius);
                var random = new Random((int)(hash * int.MaxValue));
                AddRock(b, random, center, tree.CrownRadius, color, color * 1.12f, flatten: false);
                var offset = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * tree.CrownRadius;
                AddRock(b, random, center + offset - Vector3.UnitY * tree.CrownRadius * 0.25f, tree.CrownRadius * 0.7f, color * 0.9f, color, flatten: false);
            }
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

        var ground = shoreline.Height - Underlap;
        var (top, wall) = shoreline.Kind == LandKind.Grass ? (style.GrassColor, style.GrassBankColor) : (style.QuayColor, style.QuayWallColor);
        AddPrism(b, outline, -WallDepth, ground, top.ToVector3(), wall.ToVector3());
        AddScenery(b, shoreline, style, ground);
        return b.Build(id, "Shoreline");
    }

    /// <summary>Whatever stands on the mainland: trees, crops or a town, in a band along the coast.</summary>
    private static void AddScenery(MeshBuilder b, Shoreline shoreline, LandStyle style, float ground)
    {
        var random = new Random(shoreline.ScenerySeed);
        switch (shoreline.Scenery)
        {
            case HinterlandScenery.Countryside:
                if (style.ShowTrees) AddHinterlandTrees(b, shoreline, style, random, ground, 340, 10f, SceneryDepth);
                break;

            case HinterlandScenery.Fields:
                AddFields(b, shoreline, style, random, ground);
                if (style.ShowTrees) AddHinterlandTrees(b, shoreline, style, random, ground, 60, 12f, SceneryDepth);
                break;

            case HinterlandScenery.Town:
                AddTown(b, shoreline, style, random, ground);
                if (style.ShowTrees) AddHinterlandTrees(b, shoreline, style, random, ground, 70, 18f, SceneryDepth * 0.8f);
                break;
        }
    }

    /// <summary>Trees of the same mix as a land area's, standing on the mainland rather than inside an outline.</summary>
    private static void AddHinterlandTrees(MeshBuilder b, Shoreline shoreline, LandStyle style, Random random, float ground, int count, float near, float depth)
    {
        var trees = new List<LandTree>(count);
        foreach (var (position, _, _) in ScatterInland(shoreline, random, count, near, depth))
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

            // Far enough apart to read as separate trees, and cheap to check at these counts.
            if (trees.Any(t => Vector2.Distance(t.Position, position) < (t.CrownRadius + radius) * 0.9f)) continue;
            trees.Add(new LandTree(position, height, radius, shape));
        }

        AddTrees(b, trees, ground, style);
    }

    /// <summary>Crops seen from the air: flat blocks of colour lying on the ground, lined up with the coast.</summary>
    private static void AddFields(MeshBuilder b, Shoreline shoreline, LandStyle style, Random random, float ground)
    {
        var grass = style.GrassColor.ToVector3();
        foreach (var (position, _, along) in ScatterInland(shoreline, random, 80, 20f, SceneryDepth))
        {
            var across = new Vector2(-along.Y, along.X);
            var half = new Vector2(30f + (float)random.NextDouble() * 50f, 22f + (float)random.NextDouble() * 34f);
            // Crops run from dark green through to bare, dusty earth.
            var ripeness = (float)random.NextDouble();
            var color = Vector3.Lerp(grass * 0.82f, new Vector3(0.78f, 0.70f, 0.42f), ripeness * ripeness);

            var side = along * half.X;
            var deep = across * half.Y;
            // A hair above the ground, so the field shows rather than fighting the surface for the same pixels.
            var level = ground + 0.03f;
            b.AddQuadUp(
                MarinaMath.ToWorld(position - side - deep, level),
                MarinaMath.ToWorld(position + side - deep, level),
                MarinaMath.ToWorld(position + side + deep, level),
                MarinaMath.ToWorld(position - side + deep, level),
                color);
        }
    }

    /// <summary>A town: plain blocks with roofs, standing thickest and tallest near the water.</summary>
    private static void AddTown(MeshBuilder b, Shoreline shoreline, LandStyle style, Random random, float ground)
    {
        const float depth = SceneryDepth * 0.7f;
        var walls = style.BuildingColor.ToVector3();
        var roofs = style.RoofColor.ToVector3();
        var placed = new List<(Vector2 Center, float Radius)>(140);

        foreach (var (position, inland, _) in ScatterInland(shoreline, random, 140, 25f, depth))
        {
            var seafront = 1f - Math.Clamp(inland / depth, 0f, 1f);
            var footprint = new Vector2(9f + (float)random.NextDouble() * 9f, 9f + (float)random.NextDouble() * 9f);
            var radius = MathF.Max(footprint.X, footprint.Y) * 0.5f;
            if (placed.Any(other => Vector2.Distance(other.Center, position) < other.Radius + radius + 6f)) continue;
            placed.Add((position, radius));

            var height = 5f + (float)random.NextDouble() * (5f + seafront * 14f);
            var shade = Lerp(0.86f, 1.12f, (float)random.NextDouble());
            b.AddBox(MarinaMath.ToWorld(position, ground + height * 0.5f), new Vector3(footprint.X, height, footprint.Y), walls * shade);
            b.AddBox(
                MarinaMath.ToWorld(position, ground + height + 0.6f),
                new Vector3(footprint.X * 1.12f, 1.2f, footprint.Y * 1.12f),
                roofs * Lerp(0.9f, 1.1f, (float)random.NextDouble()));
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
    /// <returns>For each place: where it is, how far inland it fell, and the direction of the coast beside it.</returns>
    private static IEnumerable<(Vector2 Position, float Inland, Vector2 Along)> ScatterInland(
        Shoreline shoreline, Random random, int count, float near, float depth)
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
            if (!shoreline.Contains(position)) continue;

            placed++;
            yield return (position, inland, direction);
        }
    }

    /// <summary>The drawn line with both ends carried on, so scenery does not end where the drawing did.</summary>
    private static IReadOnlyList<Vector2> ExtendedLine(Shoreline shoreline)
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
            b.AddTriangleFacingAway(a0, c0, c1, wallColor, inside);
            b.AddTriangleFacingAway(a0, c1, a1, wallColor, inside);
        }
    }

    /// <summary>An irregular, flattened low-poly ball.</summary>
    private static void AddRock(MeshBuilder b, Random random, Vector3 center, float radius, Vector3 dark, Vector3 light, bool flatten = true)
    {
        const int segments = 6;
        const int rings = 3;
        var radii = new Vector3(
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()),
            radius * (flatten ? Lerp(0.6f, 0.85f, (float)random.NextDouble()) : Lerp(0.85f, 1.05f, (float)random.NextDouble())),
            radius * Lerp(0.9f, 1.25f, (float)random.NextDouble()));
        var yaw = (float)random.NextDouble() * MathF.Tau;
        var color = Vector3.Lerp(dark, light, (float)random.NextDouble()) *
            new Vector3(1f, 1f, Lerp(0.94f, 1.02f, (float)random.NextDouble()));

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
