using System.Numerics;
using System.Text.Json.Nodes;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Camera;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Rendering;
using VirtualMarina.Core.Serialization;
using VirtualMarina.SampleData;
using VirtualMarina.TestSupport;

namespace VirtualMarina.Core.Tests;

/// <summary>
/// Saved marina files of every format version, checked in under Fixtures/format-&lt;version&gt;/. A file a customer
/// saved years ago has to keep opening, so these are never regenerated to make a test pass: a failure here means a
/// change broke reading an existing file.
/// </summary>
/// <remarks>
/// <para>
/// The format-1.0 files are hand-written in the old vocabulary (docks, slips, and "berths" for what are now
/// multi-berths). The format-2.0 files were written by this code: <see cref="SmallHarbor"/> and the sample marina,
/// on a fixed clock. When the format moves to a new version, add a format-&lt;new&gt; folder next to them rather than
/// replacing these; <c>VM_WRITE_FIXTURES=1</c> writes the current-version files into the source tree.
/// </para>
/// </remarks>
public class GoldenFileTests
{
    private const string CurrentFolder = "format-2.0";

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var folder in Directory.GetDirectories(Path.Combine(AppContext.BaseDirectory, "Fixtures")).Order(StringComparer.Ordinal))
        {
            foreach (var file in Directory.GetFiles(folder, "*.marina.json").Order(StringComparer.Ordinal))
            {
                data.Add(Path.GetFileName(folder), Path.GetFileName(file));
            }
        }

        return data;
    }

    internal static string ReadFixture(string folder, string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, file));

    [Fact]
    public void EveryFormatVersionHasFixtures()
    {
        var folders = Fixtures().Select(row => (string)row[0]).Distinct().ToArray();
        Assert.Contains("format-1.0", folders);
        Assert.Contains(CurrentFolder, folders);
        Assert.Equal(CurrentFolder, $"format-{MarinaDocument.CurrentVersion.ToString(2)}");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AFixture_LoadsAsItsOwnVersion_AndIsAValidMarina(string folder, string file)
    {
        var document = MarinaDocument.Parse(ReadFixture(folder, file));

        Assert.Equal("format-" + document.Version.ToString(2), folder);
        Assert.Empty(document.Validate());
        Assert.NotEmpty(document.Layout.Piers);
        Assert.NotEmpty(document.Layout.Berths);

        var marina = new MarinaVisualizer();
        document.ApplyTo(marina);
        Assert.Equal(document.Layout.Berths.Count, marina.GetBerths().Count);
        Assert.NotNull(marina.BuildRenderFrame());
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AFixture_SavedAgain_ReadsBackTheSame_AndSavesTheSameAgain(string folder, string file)
    {
        var first = MarinaDocument.Parse(ReadFixture(folder, file));
        var saved = first.ToJson();
        var second = MarinaDocument.Parse(saved);

        Assert.Equal(MarinaDocument.CurrentVersion, second.Version);
        Assert.Equal(first.Layout.Berths.Count, second.Layout.Berths.Count);
        Assert.Equal(first.Layout.MultiBerths.Count, second.Layout.MultiBerths.Count);
        Assert.Equal(saved, second.ToJson());
    }

    /// <summary>
    /// A current-version file, read and written again, keeps every value it had. The writer may add a setting the
    /// fixture predates, but must not drop or change one it has: that would silently lose part of someone's design.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ACurrentVersionFixture_SavedAgain_KeepsEveryValueItHad(string folder, string file)
    {
        if (folder != CurrentFolder) return;

        var original = JsonNode.Parse(ReadFixture(folder, file))!;
        var resaved = JsonNode.Parse(MarinaDocument.Parse(original.ToJsonString()).ToJson())!;

        var differences = new List<string>();
        CollectLost(original, resaved, "", differences);
        Assert.True(differences.Count == 0, "Lost or changed on saving again:\n" + string.Join("\n", differences.Take(20)));
    }

    private static void CollectLost(JsonNode? expected, JsonNode? actual, string path, List<string> differences)
    {
        switch (expected)
        {
            case JsonObject obj:
                if (actual is not JsonObject other)
                {
                    differences.Add($"{path}: was an object, now {actual?.ToJsonString() ?? "missing"}");
                    return;
                }

                foreach (var (key, value) in obj)
                {
                    if (!other.TryGetPropertyValue(key, out var counterpart)) differences.Add($"{path}/{key}: dropped");
                    else CollectLost(value, counterpart, $"{path}/{key}", differences);
                }

                break;
            case JsonArray array:
                if (actual is not JsonArray otherArray || otherArray.Count != array.Count)
                {
                    differences.Add($"{path}: had {array.Count} items, now {actual?.ToJsonString() ?? "missing"}");
                    return;
                }

                for (var i = 0; i < array.Count; i++) CollectLost(array[i], otherArray[i], $"{path}/{i}", differences);
                break;
            default:
                if (expected?.ToJsonString() != actual?.ToJsonString())
                {
                    differences.Add($"{path}: {expected?.ToJsonString() ?? "null"} became {actual?.ToJsonString() ?? "missing"}");
                }

                break;
        }
    }

    // ---- Writing the current-version fixtures -----------------------------------------------------

    /// <summary>
    /// Writes the current-version fixtures into the source tree when <c>VM_WRITE_FIXTURES=1</c>; otherwise checks
    /// only that the code that writes them still runs. Only for a new format version: existing fixtures stay.
    /// </summary>
    [Fact]
    public void CurrentVersionFixtures_CanBeWritten()
    {
        var files = new Dictionary<string, string>
        {
            ["small-harbor.marina.json"] = SmallHarbor().ToJson(),
            ["sample-marina.marina.json"] = SampleMarina().ToJson(),
        };

        Assert.All(files.Values, json => Assert.Contains($"\"formatVersion\": \"{MarinaDocument.CurrentVersion.ToString(2)}\"", json, StringComparison.Ordinal));
        if (Environment.GetEnvironmentVariable("VM_WRITE_FIXTURES") != "1") return;

        var folder = Path.Combine(RepositoryRoot.Path, "tests", "VirtualMarina.Core.Tests", "Fixtures", CurrentFolder);
        Directory.CreateDirectory(folder);
        foreach (var (name, json) in files) File.WriteAllText(Path.Combine(folder, name), json.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static readonly DateTimeOffset Saved = FixedClock.Default.GetUtcNow();

    /// <summary>A small marina that still touches most of the format: a multi-berth, land with trees, style, camera, presets, metadata.</summary>
    private static MarinaDocument SmallHarbor()
    {
        var pier = new Pier("A", "Pier A", Vector2.Zero, 0f, 40f, 3f, PierType.FloatingConcrete)
        {
            Services = PierServices.PowerAndWater,
            Metadata = new Dictionary<string, string> { ["erp"] = "PIER-0001" },
        };
        var boat = new Boat("B-1", "Aurora", BoatType.MotorYacht) { LengthMeters = 16f, BeamMeters = 5f, OwnerName = "M. Rossi" };
        var berths = new List<Berth>();
        for (var i = 0; i < 4; i++)
        {
            berths.Add(BerthGenerator.AtPier(pier, $"A-L{i + 1:00}", PierSide.Left, i * 6f, 5.5f, 12f));
            berths.Add(BerthGenerator.AtPier(pier, $"A-R{i + 1:00}", PierSide.Right, i * 6f, 5.5f, 12f) with
            {
                Status = i % 2 == 0 ? BerthStatus.Reserved : BerthStatus.Free,
                Boat = i % 2 == 0 ? new Boat($"R-{i}", "Guest", BoatType.MonohullSailboat) { ExpectedArrival = Saved.AddDays(2) } : null,
                Metadata = new Dictionary<string, string> { ["power"] = "32A" },
            });
        }

        var quay = new LandArea("quay", new[] { new Vector2(-30, -8), new Vector2(40, -8), new Vector2(40, -30), new Vector2(-30, -30) }, 1.5f)
        {
            Name = "Quay",
        };
        quay = quay with { Trees = LandArea.GenerateTrees(quay.Points, 6f, new Random(3)) };

        var marina = new MarinaVisualizer();
        marina.InitializeLayout(new MarinaLayout { Name = "Small Harbor", Piers = [pier], Berths = berths, LandAreas = [quay] });
        marina.MoorAlongside(new[] { "A-L01", "A-L02" }, boat, multiBerthId: "VIP");
        marina.BerthLabelMode = BerthLabelMode.All;
        marina.Style.Water.WaveAmplitude = 0.05f;
        marina.Style.Status.FreeColor = ColorRgba.FromHex("#00C853");

        var document = MarinaDocument.FromVisualizer(marina, generator: "VirtualMarina fixtures", includeReferenceImage: false);
        document.Description = "Golden file: a small harbor touching most of the format.";
        document.Camera = new CameraPose(new Vector3(10, 0, -5), 30f, 40f, 120f);
        document.CameraPresets = [new CameraPreset("Fuel dock", new CameraPose(new Vector3(0, 0, 20), 0f, 35f, 60f), "Where boats refuel")];
        document.SavedUtc = Saved;
        return document;
    }

    /// <summary>The test hosts' sample marina: every pier type, land berths, single-sided piers, breakwaters.</summary>
    private static MarinaDocument SampleMarina()
    {
        var marina = new MarinaVisualizer();
        marina.InitializeLayout(MockMarinaFactory.CreateSampleMarina(seed: 42, clock: FixedClock.Default));
        var document = MarinaDocument.FromVisualizer(marina, generator: "VirtualMarina fixtures", includeCamera: false, includeReferenceImage: false);
        document.SavedUtc = Saved;
        return document;
    }
}
