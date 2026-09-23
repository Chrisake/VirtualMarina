using System.Numerics;
using System.Text.Json.Nodes;
using CsCheck;
using VirtualMarina.Core.Design;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Serialization;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Properties that hold for every input, checked against a few hundred random ones each (CsCheck). A failure is
/// shrunk to the smallest input that still fails and printed with the seed that reproduces it; pass that seed to
/// <c>Sample(seed: ...)</c> to replay it.
/// </summary>
/// <remarks>
/// Iteration counts are kept modest and fixed so the suite stays fast and a run is the same size every time; the
/// seed changes per run, so over many runs the inputs cover far more than any one of them.
/// </remarks>
public class PropertyTests
{
    // ---- The file format -------------------------------------------------------------------------

    private static readonly Gen<string> Text =
        Gen.String[Gen.Char["abcXYZ019 -_.:/'\"\\{}[]éλ⚓\u00a0\t\n"], 0, 12];

    private static readonly Gen<string> Id =
        Gen.String[Gen.Char["ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"], 1, 4];

    private static readonly Gen<Boat> ABoat =
        Gen.Select(Id, Text, Gen.Enum<BoatType>(), Gen.Float[3f, 30f], Gen.Int[0, 500], Text,
            (id, name, type, length, arrivalHours, owner) => new Boat("B" + id, name, type)
            {
                LengthMeters = MathF.Round(length, 2),
                BeamMeters = MathF.Round(length / 3f, 2),
                OwnerName = owner.Length == 0 ? null : owner,
                ExpectedArrival = arrivalHours == 0 ? null : FixedDate.AddHours(arrivalHours),
            });

    private static readonly DateTimeOffset FixedDate = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A few piers at random places, each with a random row of berths down either side.</summary>
    private static readonly Gen<MarinaLayout> ALayout =
        Gen.SelectMany(Gen.Int[1, 4], Text, (pierCount, name) =>
            Gen.Select(
                Gen.Select(Gen.Float[-200f, 200f], Gen.Float[-200f, 200f], Gen.Float[0f, 359f], Gen.Float[20f, 120f], Gen.Int[0, 8], Gen.Int[0, 8],
                        (x, y, heading, length, left, right) => (X: x, Y: y, Heading: heading, Length: length, Left: left, Right: right))
                    .Array[pierCount],
                ABoat.Array[pierCount * 16],
                Gen.Enum<BerthStatus>().Array[pierCount * 16],
                (piers, boats, statuses) => BuildLayout(name, piers, boats, statuses)));

    private static MarinaLayout BuildLayout(
        string name,
        (float X, float Y, float Heading, float Length, int Left, int Right)[] pierSpecs,
        Boat[] boats,
        BerthStatus[] statuses)
    {
        var piers = new List<Pier>();
        var berths = new List<Berth>();
        for (var p = 0; p < pierSpecs.Length; p++)
        {
            var spec = pierSpecs[p];
            var pier = new Pier($"P{p}", name, new Vector2(MathF.Round(spec.X, 2), MathF.Round(spec.Y, 2)), MathF.Round(spec.Heading, 1), MathF.Round(spec.Length, 1))
            {
                Metadata = new Dictionary<string, string> { ["note"] = name },
            };
            piers.Add(pier);

            var rows = new[] { (PierSide.Left, spec.Left), (PierSide.Right, spec.Right) };
            foreach (var (side, count) in rows)
            {
                for (var i = 0; i < count && (i + 1) * 5f <= pier.Length; i++)
                {
                    var index = berths.Count;
                    var status = statuses[index % statuses.Length];
                    var berth = BerthGenerator.AtPier(pier, $"{pier.Id}-{(side == PierSide.Left ? 'L' : 'R')}{i + 1:00}", side, i * 5f, 5f, 12f);
                    berths.Add(berth with
                    {
                        Status = status,
                        Boat = status == BerthStatus.Free ? null : boats[index % boats.Length],
                        Label = name.Length == 0 ? null : name,
                    });
                }
            }
        }

        return new MarinaLayout { Name = name.Length == 0 ? "Marina" : name, Piers = piers, Berths = berths };
    }

    [Fact]
    public void AnyLayout_WrittenAndReadBack_IsWrittenTheSameWay()
    {
        ALayout.Sample(layout =>
        {
            var document = new MarinaDocument { Name = layout.Name, Layout = layout, SavedUtc = FixedDate, Generator = "property test" };
            var json = document.ToJson();
            var reread = MarinaDocument.Parse(json);

            // Compared as JSON rather than as records: what matters is that nothing written is lost or changed.
            Assert.Equal(json, reread.ToJson());
            Assert.Equal(layout.Berths.Count, reread.Layout.Berths.Count);
            Assert.Empty(reread.Validate());
        }, iter: 60, threads: 1);
    }

    /// <summary>
    /// Whatever a damaged or hand-edited file contains, reading it either works or fails with the one exception the
    /// format documents. Mutations replace a random value in a real file with a value of a random kind, or cut the
    /// text short, which between them reach the tokenizer, the converters and the model's own validation.
    /// </summary>
    [Fact]
    public void AnyMutationOfARealFile_EitherLoadsOrThrowsMarinaFormatException()
    {
        var original = GoldenFileTests.ReadFixture("format-2.0", "small-harbor.marina.json");
        Assert.Empty(MarinaDocument.Parse(original).Validate());
        var paths = ValuePaths(JsonNode.Parse(original)!).ToArray();

        var mutation = Gen.Select(Gen.Int[0, paths.Length - 1], Gen.Int[0, 7], Gen.Int[0, 9], Text,
            (path, kind, count, text) => (path, kind, count, text));

        mutation.Sample(m =>
        {
            var json = Mutate(original, paths[m.path], m.kind, m.count, m.text);
            try
            {
                _ = MarinaDocument.Parse(json);
            }
            catch (MarinaFormatException)
            {
                // The documented failure.
            }
        }, iter: 400, threads: 1, print: m => Mutate(original, paths[m.path], m.kind, m.count, m.text));
    }

    /// <summary>
    /// A null in place of a list entry or a required id. Reading is tolerant (a null entry is dropped, a null id reads
    /// as a missing one), so the file may load; what it must never do is fail with anything but MarinaFormatException.
    /// </summary>
    [Theory]
    [InlineData("/layout/landAreas/0")]
    [InlineData("/layout/landAreas/0/id")]
    [InlineData("/layout/landAreas/0/trees/0")]
    [InlineData("/layout/piers/0")]
    [InlineData("/layout/piers/0/id")]
    [InlineData("/layout/berths/0")]
    [InlineData("/layout/berths/0/id")]
    [InlineData("/layout/multiBerths/0")]
    [InlineData("/layout/multiBerths/0/id")]
    [InlineData("/layout/multiBerths/0/boat/id")]
    [InlineData("/cameraPresets/0")]
    public void ANullWhereTheFormatNeedsAValue_LoadsOrThrowsMarinaFormatException(string path)
    {
        var json = Mutate(GoldenFileTests.ReadFixture("format-2.0", "small-harbor.marina.json"), path, kind: 0, count: 0, text: "");
        var error = Record.Exception(() => MarinaDocument.Parse(json));
        Assert.True(error is null or MarinaFormatException, $"Unexpected {error?.GetType().Name}: {error?.Message}");
    }

    private static string Mutate(string original, string path, int kind, int count, string text)
    {
        if (kind == 7) return original[..(original.Length * count / 10)];

        var root = JsonNode.Parse(original)!;
        JsonNode? replacement = kind switch
        {
            0 => null,
            1 => JsonValue.Create(text),
            2 => JsonValue.Create(count - 5),
            3 => JsonValue.Create(count % 2 == 0 ? double.MaxValue : -1e-30),
            4 => JsonValue.Create(count % 2 == 0),
            5 => new JsonArray(Enumerable.Range(0, count).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
            _ => new JsonObject { [text] = count },
        };

        var target = root;
        var steps = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < steps.Length - 1; i++) target = Step(target, steps[i]);
        var last = steps[^1];
        if (target is JsonArray array) array[int.Parse(last, System.Globalization.CultureInfo.InvariantCulture)] = replacement;
        else target.AsObject()[last] = replacement;
        return root.ToJsonString();
    }

    private static JsonNode Step(JsonNode node, string step) =>
        node is JsonArray array ? array[int.Parse(step, System.Globalization.CultureInfo.InvariantCulture)]! : node[step]!;

    /// <summary>Every position in the document that holds a value, as a slash-separated path.</summary>
    private static IEnumerable<string> ValuePaths(JsonNode node, string prefix = "")
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                {
                    var path = prefix + "/" + key;
                    yield return path;
                    if (value is not null)
                    {
                        foreach (var inner in ValuePaths(value, path)) yield return inner;
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var path = prefix + "/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    yield return path;
                    if (array[i] is { } value)
                    {
                        foreach (var inner in ValuePaths(value, path)) yield return inner;
                    }
                }

                break;
        }
    }

    // ---- Polygons --------------------------------------------------------------------------------

    /// <summary>
    /// A star-shaped outline around the origin (every point visible from the centre, so it is simple whatever the
    /// radii), with up to two square holes well inside its smallest radius.
    /// </summary>
    private static readonly Gen<(Vector2[] Outer, Vector2[][] Holes)> AShapeWithHoles =
        // Five corners or more keeps every gap between neighbouring corners under half a turn, so the centre is inside.
        Gen.SelectMany(Gen.Int[5, 14], Gen.Int[0, 2], (corners, holeCount) =>
            Gen.Select(Gen.Float[0f, 1f].Array[corners], Gen.Float[20f, 60f].Array[corners], Gen.Bool, (angles, radii, clockwise) =>
            {
                // Distinct, ordered angles: each corner gets its own sector of the circle.
                var outer = new Vector2[corners];
                for (var i = 0; i < corners; i++)
                {
                    var angle = (i + 0.1f + angles[i] * 0.8f) / corners * MathF.Tau;
                    outer[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radii[i];
                }

                if (clockwise) Array.Reverse(outer);

                // The largest circle the outline certainly contains: the smallest radius, shrunk for the chord
                // between neighbouring corners.
                var inner = radii.Min() * MathF.Cos(MathF.PI / corners * 1.8f);
                var holes = Enumerable.Range(0, holeCount).Select(h =>
                {
                    var centre = new Vector2(holeCount == 1 ? 0f : (h == 0 ? -0.45f : 0.45f) * inner, 0f);
                    var half = inner * 0.2f;
                    return new[]
                    {
                        centre + new Vector2(-half, -half), centre + new Vector2(half, -half),
                        centre + new Vector2(half, half), centre + new Vector2(-half, half),
                    };
                }).ToArray();

                return (outer, holes);
            }));

    [Fact]
    public void TriangulatingWithHoles_CoversTheOutlineLessTheHoles_WithTrianglesInsideIt()
    {
        AShapeWithHoles.Sample(shape =>
        {
            var holes = shape.Holes.Select(h => (IReadOnlyList<Vector2>)h).ToArray();
            var (points, triangles) = PolygonMath.TriangulateWithHoles(shape.Outer, holes);

            var expected = MathF.Abs(PolygonMath.SignedArea(shape.Outer)) - holes.Sum(h => MathF.Abs(PolygonMath.SignedArea(h)));
            var covered = 0f;
            foreach (var (a, b, c) in triangles)
            {
                var area = Cross(points[b] - points[a], points[c] - points[a]) * 0.5f;
                Assert.True(area >= -1e-3f, $"triangle ({a}, {b}, {c}) is wound clockwise (area {area})");
                covered += area;

                var centroid = (points[a] + points[b] + points[c]) / 3f;
                if (area > 1e-2f)
                {
                    Assert.True(PolygonMath.Contains(shape.Outer, centroid), $"triangle ({a}, {b}, {c}) lies outside the outline");
                    Assert.DoesNotContain(holes, hole => PolygonMath.Contains(hole, centroid));
                }
            }

            Assert.Equal(expected, covered, expected * 1e-3f + 1e-2f);
        }, iter: 300, threads: 1);
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// IsSimple against a brute-force oracle in exact integer arithmetic. Points on a small grid make crossings,
    /// shared corners, T-junctions and collinear overlaps common, and every distance is either zero or far above the
    /// millimetre IsSimple treats as touching, so the two must agree exactly.
    /// </summary>
    [Fact]
    public void IsSimple_AgreesWithABruteForceOracle()
    {
        var polygon = Gen.Select(Gen.Int[0, 6], Gen.Int[0, 6], (x, y) => (X: (long)x, Y: (long)y)).Array[3, 7];
        polygon.Sample(points =>
        {
            var vectors = points.Select(p => new Vector2(p.X, p.Y)).ToArray();
            Assert.Equal(IsSimpleOracle(points), PolygonMath.IsSimple(vectors));
        }, iter: 2000, threads: 1);
    }

    private static bool IsSimpleOracle((long X, long Y)[] p)
    {
        var n = p.Length;
        if (n < 3) return false;
        for (var i = 0; i < n; i++)
        {
            var a1 = p[i];
            var a2 = p[(i + 1) % n];
            var a3 = p[(i + 2) % n];
            if (a1 == a2) return false;

            // Neighbouring edges share exactly their common corner, and neither doubles back over the other.
            if (OnSegment(a3, a1, a2) || OnSegment(a1, a2, a3)) return false;

            for (var j = 0; j < n; j++)
            {
                var adjacent = j == i || j == (i + 1) % n || (j + 1) % n == i;
                if (!adjacent && Intersect(a1, a2, p[j], p[(j + 1) % n])) return false;
            }
        }

        return true;
    }

    private static long Orientation((long X, long Y) from, (long X, long Y) to, (long X, long Y) point) =>
        Math.Sign((to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X));

    private static bool OnSegment((long X, long Y) q, (long X, long Y) a, (long X, long Y) b) =>
        Orientation(a, b, q) == 0
        && Math.Min(a.X, b.X) <= q.X && q.X <= Math.Max(a.X, b.X)
        && Math.Min(a.Y, b.Y) <= q.Y && q.Y <= Math.Max(a.Y, b.Y);

    private static bool Intersect((long X, long Y) a, (long X, long Y) b, (long X, long Y) c, (long X, long Y) d)
    {
        var o1 = Orientation(a, b, c);
        var o2 = Orientation(a, b, d);
        var o3 = Orientation(c, d, a);
        var o4 = Orientation(c, d, b);
        if (o1 != o2 && o3 != o4) return true;
        return OnSegment(c, a, b) || OnSegment(d, a, b) || OnSegment(a, c, d) || OnSegment(b, c, d);
    }

    // ---- Berth naming ----------------------------------------------------------------------------

    /// <summary>
    /// Reading the pattern back out of a name the scheme wrote, and writing with that pattern, gives the same name:
    /// what the designer shows as "the naming on this pier" reproduces the berths that are there.
    /// </summary>
    [Fact]
    public void ANameWrittenByAScheme_ReadBackAsAPattern_WritesTheSameName()
    {
        var scheme = Gen.Select(
            Gen.OneOfConst("{pier}-{side}{number}", "{pier}{side}{number}", "{pier}.{side}.{number}", "{side}{number}", "{number}", "Berth {number}", "{pier}/{number}"),
            Gen.Int[1, 4],
            Gen.OneOfConst(("L", "R"), ("P", "S"), ("Port", "Stbd"), ("W", "E")),
            (pattern, digits, sides) => new BerthNamingScheme { Pattern = pattern, NumberDigits = digits, LeftSide = sides.Item1, RightSide = sides.Item2 });

        // Pier ids made of letters none of the literal text or side tokens contain: the pier id and the side token are
        // looked for case-insensitively, which does not round-trip (see the test below).
        var pierId = Gen.String[Gen.Char["ACFGJKMNQUVXYZ0123456789-"], 1, 4].Where(id => id.Trim().Length > 0 && !char.IsAsciiDigit(id[^1]));

        Gen.Select(scheme, pierId, Gen.Bool, Gen.Int[0, 20000]).Sample((naming, id, left, number) =>
        {
            var pier = new Pier(id, id, Vector2.Zero, 0f, 40f);
            var side = left ? PierSide.Left : PierSide.Right;
            var name = naming.Format(pier, side, number);

            var inferred = naming.Infer(pier, side, name);
            Assert.NotNull(inferred);
            var again = naming with { Pattern = inferred.Value.Pattern, NumberDigits = inferred.Value.Digits };
            Assert.Equal(name, again.Format(pier, side, number));
        }, iter: 1000, threads: 1);
    }

    /// <summary>
    /// Found by the property above: Infer matches the pier id and the side token ignoring case, then writes them back
    /// in the pier's and the scheme's own case, so literal text that merely resembles them changes case.
    /// </summary>
    [Theory]
    [InlineData("Berth {number}", "BE", "L", "Berth 07")]
    [InlineData("{pier}{number}", "Al", "L", "Al07")]
    public void InferringFromANameThatOnlyResemblesThePierOrSide_KeepsItsCase(string pattern, string pierId, string leftSide, string expected)
    {
        var naming = new BerthNamingScheme { Pattern = pattern, LeftSide = leftSide };
        var pier = new Pier(pierId, pierId, Vector2.Zero, 0f, 40f);
        var name = naming.Format(pier, PierSide.Left, 7);
        Assert.Equal(expected, name);

        var inferred = naming.Infer(pier, PierSide.Left, name)!.Value;
        Assert.Equal(name, (naming with { Pattern = inferred.Pattern, NumberDigits = inferred.Digits }).Format(pier, PierSide.Left, 7));
    }
}
