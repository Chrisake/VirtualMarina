using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Picking;

/// <summary>A boat as drawn in the scene: in one slip, or spanning the slips of a <see cref="MultiSlipBerth"/>.</summary>
/// <param name="Boat">The boat.</param>
/// <param name="Status">Status of the slip(s).</param>
/// <param name="World">Model-to-world transform.</param>
/// <param name="Slips">The slip, or the berth's member slips in berth order (the first is the primary slip).</param>
/// <param name="BerthId">The multi-slip berth, if any.</param>
/// <param name="Ground">Height of the land the boat rests on, or null when it floats on the water.</param>
internal readonly record struct BoatInstance(Boat Boat, SlipStatus Status, Matrix4x4 World, IReadOnlyList<Slip> Slips, string? BerthId, float? Ground = null)
{
    public bool OnLand => Ground.HasValue;

    public Slip PrimarySlip => Slips[0];

    /// <summary>Drawn grayed out when every visible slip it occupies is disabled.</summary>
    public bool IsDisabled => Slips.Where(s => s.IsVisible).All(s => s.IsDisabled);
}

/// <summary>
/// Where things sit inside a slip. The scene builder and the picker share this so what you click
/// always matches what you see.
/// </summary>
internal static class SlipPlacement
{
    /// <summary>Height of the translucent status pad above the calm water plane.</summary>
    public const float PadHeight = 0.22f;

    /// <summary>Height of a land slip's status pad above the land surface.</summary>
    public const float LandPadLift = 0.04f;

    /// <summary>Height of the cradle stands a boat on land rests on (keel above the land surface).</summary>
    public const float CradleHeight = 0.7f;

    /// <summary>Clearance between the boat and the dock end of the slip.</summary>
    private const float BowClearance = 0.8f;

    /// <summary>Height of the status pad: just above the water, or just above the land for a land slip (<paramref name="ground"/>).</summary>
    public static float PadHeightFor(float? ground) => ground.HasValue ? ground.Value + LandPadLift : PadHeight;

    /// <param name="slip">The slip.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="ground">Land height for a land slip; null on the water.</param>
    /// <param name="meshes">Boat meshes, used to rest a boat on land on its keel.</param>
    public static Matrix4x4 BoatWorld(Slip slip, Boat boat, float? ground = null, MeshLibrary? meshes = null)
    {
        if (ground.HasValue)
        {
            // Ashore: centered on the spot, raised on its cradle.
            return Place(boat, slip.HeadingDegrees, slip.Center, BoatBaseHeight(boat, ground, meshes));
        }

        // Sit the boat toward the dock end of the slip, leaving a little clearance.
        var slack = slip.Length - boat.LengthMeters - BowClearance;
        var along = slack > 0f ? slack * 0.5f : 0f;
        return Place(boat, slip.HeadingDegrees, slip.Center + slip.Forward * along);
    }

    /// <summary>Placement of a boat spanning several slips, in the frame of the first slip.</summary>
    public static Matrix4x4 BerthBoatWorld(IReadOnlyList<Slip> slips, Boat boat, MooringStyle style, float? ground = null, MeshLibrary? meshes = null)
    {
        var (center, width, length, reference) = CombinedFrame(slips);
        var y = BoatBaseHeight(boat, ground, meshes);
        if (style == MooringStyle.Alongside)
        {
            // Parallel to the dock (along the slips' right axis), beam toward the dock end.
            var slack = length - boat.BeamMeters - BowClearance;
            var along = slack > 0f && !ground.HasValue ? slack * 0.5f : 0f;
            return Place(boat, reference.HeadingDegrees + 90f, center + reference.Forward * along, y);
        }

        var bowSlack = length - boat.LengthMeters - BowClearance;
        var bowAlong = bowSlack > 0f && !ground.HasValue ? bowSlack * 0.5f : 0f;
        return Place(boat, reference.HeadingDegrees, center + reference.Forward * bowAlong, y);
    }

    /// <summary>World Y of the boat model's origin: the waterline on the water, or high enough to put the keel on the cradle ashore.</summary>
    public static float BoatBaseHeight(Boat boat, float? ground, MeshLibrary? meshes) =>
        ground.HasValue ? ground.Value + CradleHeight + KeelDepth(boat, meshes) : 0f;

    /// <summary>How far the boat model reaches below its waterline, in meters.</summary>
    public static float KeelDepth(Boat boat, MeshLibrary? meshes)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(boat.Type);
        var scale = boat.LengthMeters / nominal.Length;
        return meshes is not null && meshes.TryGet(MeshIds.ForBoat(boat.Type), out var mesh)
            ? MathF.Max(0f, -mesh.Bounds.Min.Y * scale)
            : 0.5f * scale;
    }

    /// <summary>
    /// Center and size of the rectangle covering all <paramref name="slips"/>, measured along the first slip's
    /// right (width) and forward (length) axes.
    /// </summary>
    public static (Vector2 Center, float Width, float Length, Slip Reference) CombinedFrame(IReadOnlyList<Slip> slips)
    {
        var reference = slips[0];
        var right = reference.Right;
        var forward = reference.Forward;
        float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
        foreach (var slip in slips)
        {
            foreach (var corner in slip.Bounds.GetCorners())
            {
                var rel = corner - reference.Center;
                var r = Vector2.Dot(rel, right);
                var f = Vector2.Dot(rel, forward);
                minR = MathF.Min(minR, r);
                maxR = MathF.Max(maxR, r);
                minF = MathF.Min(minF, f);
                maxF = MathF.Max(maxF, f);
            }
        }

        var center = reference.Center + right * ((minR + maxR) * 0.5f) + forward * ((minF + maxF) * 0.5f);
        return (center, maxR - minR, maxF - minF, reference);
    }

    /// <summary>World Y of the top of the boat (above the water, or above its cradle ashore when <paramref name="ground"/> is set).</summary>
    public static float BoatTopHeight(Boat boat, MeshLibrary meshes, float? ground = null)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(boat.Type);
        var scale = boat.LengthMeters / nominal.Length;
        var top = meshes.TryGet(MeshIds.ForBoat(boat.Type), out var mesh) ? mesh.Bounds.Max.Y * scale : 3f;
        return top + BoatBaseHeight(boat, ground, meshes);
    }

    public static Matrix4x4 PadWorld(Slip slip, float? ground = null) =>
        MarinaMath.CreatePlacement(
            new Vector3(MathF.Max(0.2f, slip.Width - 0.3f), 1f, MathF.Max(0.2f, slip.Length - 0.3f)),
            slip.HeadingDegrees,
            MarinaMath.ToWorld(slip.Center, PadHeightFor(ground)));

    /// <summary>Status buoy at the seaward end of the slip.</summary>
    public static Vector3 BuoyPosition(Slip slip) =>
        MarinaMath.ToWorld(slip.Center - slip.Forward * (slip.Length * 0.5f - 0.6f), 0.3f);

    /// <summary>Height of a land slip's status post above the land (the status ball sits on top).</summary>
    public const float StatusPostHeight = 1.6f;

    /// <summary>Status post at the rear end of a land slip (the counterpart of the water slip's buoy).</summary>
    public static Vector2 StatusPostPosition(Slip slip) => slip.Center - slip.Forward * (slip.Length * 0.5f - 0.5f);

    /// <summary>Largest label height, in meters.</summary>
    public const float MaxLabelHeight = 1.0f;

    /// <summary>Fraction of the slip width a label may use, leaving a clear gap to the neighbours' labels.</summary>
    public const float LabelWidthFraction = 0.65f;

    /// <summary>Smallest label height, in meters; longer names are allowed to overflow the slip width.</summary>
    public const float MinLabelHeight = 0.3f;

    /// <summary>
    /// Where a slip's name is written: flat on the water just past the slip's open (seaward) end, centered across
    /// the slip and sized so the text fits within its width. The text's top points away from the dock, so it reads
    /// upright to someone standing on the dock looking at the slip.
    /// </summary>
    /// <returns>Center of the text, glyph height, heading of the text's "up" direction and the reading direction.</returns>
    public static (Vector2 Center, float Height, float UpHeadingDegrees, Vector2 ReadingDirection) LabelPlacement(Slip slip, int characterCount)
    {
        var available = MathF.Max(0.5f, slip.Width * LabelWidthFraction);
        var height = Math.Clamp(available / MathF.Max(GlyphFont.MeasureWidth(characterCount), 0.01f), MinLabelHeight, MaxLabelHeight);
        const float gap = 0.35f;
        var center = slip.Center - slip.Forward * (slip.Length * 0.5f + gap + height * 0.5f);
        var upHeading = slip.HeadingDegrees + 180f;
        // Glyph meshes read along their local −X, which maps to −Right(upHeading).
        return (center, height, upHeading, -MarinaMath.HeadingToRight(upHeading));
    }

    public static float AnimationPhase(string slipId) => MarinaMath.StableHash01(slipId) * MathF.Tau;

    /// <summary>
    /// Every boat drawn for the given slips: one per ordinary slip with a boat, and one per multi-slip berth.
    /// Hidden slips and slips excluded by <paramref name="filter"/> contribute nothing.
    /// </summary>
    /// <param name="slips">Slips to draw boats for.</param>
    /// <param name="slipLookup">Resolves berth member slips.</param>
    /// <param name="berthLookup">Resolves multi-slip berths.</param>
    /// <param name="filter">Status filter.</param>
    /// <param name="groundHeight">Land height of a land slip (null for water slips); when null every boat floats.</param>
    /// <param name="meshes">Boat meshes, used to rest boats on land on their keels.</param>
    public static IEnumerable<BoatInstance> EnumerateBoats(
        IEnumerable<Slip> slips,
        Func<string, Slip?> slipLookup,
        Func<string, MultiSlipBerth?> berthLookup,
        SlipStatusFilter filter,
        Func<Slip, float?>? groundHeight = null,
        MeshLibrary? meshes = null)
    {
        HashSet<string>? emittedBerths = null;
        foreach (var slip in slips)
        {
            if (!slip.IsVisible || !filter.Includes(slip.Status) || slip.Boat is null || !slip.Status.CanHaveBoat()) continue;

            if (slip.BerthId is { } berthId && berthLookup(berthId) is { } berth)
            {
                emittedBerths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!emittedBerths.Add(berth.Id)) continue;

                var members = berth.SlipIds.Select(slipLookup).OfType<Slip>().ToArray();
                if (members.Length == 0) continue;
                var berthGround = groundHeight?.Invoke(members[0]);
                yield return new BoatInstance(
                    berth.Boat, berth.Status, BerthBoatWorld(members, berth.Boat, berth.Style, berthGround, meshes), members, berth.Id, berthGround);
            }
            else
            {
                var ground = groundHeight?.Invoke(slip);
                yield return new BoatInstance(slip.Boat, slip.Status, BoatWorld(slip, slip.Boat, ground, meshes), new[] { slip }, null, ground);
            }
        }
    }

    private static Matrix4x4 Place(Boat boat, float headingDegrees, Vector2 center, float y = 0f)
    {
        var nominal = BoatTypeCatalog.GetNominalDimensions(boat.Type);
        var lengthScale = boat.LengthMeters / nominal.Length;
        var beamScale = boat.BeamMeters / nominal.Beam;
        return MarinaMath.CreatePlacement(new Vector3(beamScale, lengthScale, lengthScale), headingDegrees, MarinaMath.ToWorld(center, y));
    }
}
