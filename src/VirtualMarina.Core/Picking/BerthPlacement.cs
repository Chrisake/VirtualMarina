using System.Numerics;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Picking;

/// <summary>A boat as drawn in the scene: in one berth, or spanning the berths of a <see cref="MultiBerth"/>.</summary>
/// <param name="Boat">The boat.</param>
/// <param name="Status">Status of the berth(s).</param>
/// <param name="World">Model-to-world transform.</param>
/// <param name="Berths">The berth, or the berth's member berths in berth order (the first is the primary berth).</param>
/// <param name="MultiBerthId">The multi-berth, if any.</param>
/// <param name="Ground">Height of the land the boat rests on, or null when it floats on the water.</param>
internal readonly record struct BoatInstance(Boat Boat, BerthStatus Status, Matrix4x4 World, IReadOnlyList<Berth> Berths, string? MultiBerthId, float? Ground = null)
{
    public bool OnLand => Ground.HasValue;

    public Berth PrimaryBerth => Berths[0];

    /// <summary>Drawn grayed out when every visible berth it occupies is disabled.</summary>
    public bool IsDisabled => Berths.Where(s => s.IsVisible).All(s => s.IsDisabled);
}

/// <summary>
/// Where things sit inside a berth. The scene builder and the picker share this so what you click
/// always matches what you see.
/// </summary>
internal static class BerthPlacement
{
    /// <summary>Height of the translucent status pad above the calm water plane.</summary>
    public const float PadHeight = 0.22f;

    /// <summary>Height of a land berth's status pad above the land surface.</summary>
    public const float LandPadLift = 0.04f;

    /// <summary>Height of the cradle stands a boat on land rests on (keel above the land surface).</summary>
    public const float CradleHeight = 0.7f;

    /// <summary>Clearance between the boat and the pier end of the berth.</summary>
    private const float BowClearance = 0.8f;

    /// <summary>Height of the status pad: just above the water, or just above the land for a land berth (<paramref name="ground"/>).</summary>
    public static float PadHeightFor(float? ground) => ground.HasValue ? ground.Value + LandPadLift : PadHeight;

    /// <param name="berth">The berth.</param>
    /// <param name="boat">The boat.</param>
    /// <param name="ground">Land height for a land berth; null on the water.</param>
    /// <param name="meshes">Boat meshes, used to rest a boat on land on its keel.</param>
    public static Matrix4x4 BoatWorld(Berth berth, Boat boat, float? ground = null, MeshLibrary? meshes = null)
    {
        if (ground.HasValue)
        {
            // Ashore: centered on the spot, raised on its cradle.
            return Place(boat, berth.HeadingDegrees, berth.Center, BoatBaseHeight(boat, ground, meshes));
        }

        // Sit the boat toward the pier end of the berth, leaving a little clearance.
        var slack = berth.Length - boat.LengthMeters - BowClearance;
        var along = slack > 0f ? slack * 0.5f : 0f;
        return Place(boat, berth.HeadingDegrees, berth.Center + berth.Forward * along);
    }

    /// <summary>Placement of a boat spanning several berths, in the frame of the first berth.</summary>
    public static Matrix4x4 MultiBerthBoatWorld(IReadOnlyList<Berth> berths, Boat boat, MooringStyle style, float? ground = null, MeshLibrary? meshes = null)
    {
        var (center, width, length, reference) = CombinedFrame(berths);
        var y = BoatBaseHeight(boat, ground, meshes);
        if (style == MooringStyle.Alongside)
        {
            // Parallel to the pier (along the berths' right axis), beam toward the pier end.
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
    /// Center and size of the rectangle covering all <paramref name="berths"/>, measured along the first berth's
    /// right (width) and forward (length) axes.
    /// </summary>
    public static (Vector2 Center, float Width, float Length, Berth Reference) CombinedFrame(IReadOnlyList<Berth> berths)
    {
        var reference = berths[0];
        var right = reference.Right;
        var forward = reference.Forward;
        float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
        foreach (var berth in berths)
        {
            foreach (var corner in berth.Bounds.GetCorners())
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

    public static Matrix4x4 PadWorld(Berth berth, float? ground = null) =>
        MarinaMath.CreatePlacement(
            new Vector3(MathF.Max(0.2f, berth.Width - 0.3f), 1f, MathF.Max(0.2f, berth.Length - 0.3f)),
            berth.HeadingDegrees,
            MarinaMath.ToWorld(berth.Center, PadHeightFor(ground)));

    /// <summary>Status buoy at the seaward end of the berth.</summary>
    public static Vector3 BuoyPosition(Berth berth) =>
        MarinaMath.ToWorld(berth.Center - berth.Forward * (berth.Length * 0.5f - 0.6f), 0.3f);

    /// <summary>Height of a land berth's status post above the land (the status ball sits on top).</summary>
    public const float StatusPostHeight = 1.6f;

    /// <summary>Status post at the rear end of a land berth (the counterpart of the water berth's buoy).</summary>
    public static Vector2 StatusPostPosition(Berth berth) => berth.Center - berth.Forward * (berth.Length * 0.5f - 0.5f);

    /// <summary>Largest label height, in meters.</summary>
    public const float MaxLabelHeight = 1.0f;

    /// <summary>Fraction of the berth width a label may use, leaving a clear gap to the neighbours' labels.</summary>
    public const float LabelWidthFraction = 0.65f;

    /// <summary>Smallest label height, in meters; longer names are allowed to overflow the berth width.</summary>
    public const float MinLabelHeight = 0.3f;

    /// <summary>
    /// Where a berth's name is written: flat on the water just past the berth's open (seaward) end, centered across
    /// the berth and sized so the text fits within its width. The text's top points away from the pier, so it reads
    /// upright to someone standing on the pier looking at the berth.
    /// </summary>
    /// <returns>Center of the text, glyph height, heading of the text's "up" direction and the reading direction.</returns>
    public static (Vector2 Center, float Height, float UpHeadingDegrees, Vector2 ReadingDirection) LabelPlacement(Berth berth, int characterCount) =>
        LabelPlacement(berth, characterCount, LabelFont.Regular);

    /// <summary>Where a berth's label sits, for text set in a particular face.</summary>
    /// <param name="berth">The berth.</param>
    /// <param name="characterCount">How many characters the label has.</param>
    /// <param name="font">The face the label is set in, which decides how wide it runs.</param>
    /// <returns>Center of the text, glyph height, heading of the text's "up" direction and the reading direction.</returns>
    public static (Vector2 Center, float Height, float UpHeadingDegrees, Vector2 ReadingDirection) LabelPlacement(Berth berth, int characterCount, LabelFont font) =>
        LabelPlacementForWidth(berth, GlyphFont.MeasureWidth(characterCount, font));

    /// <summary>Where a berth's label sits, for text of a width that has already been measured.</summary>
    /// <param name="berth">The berth.</param>
    /// <param name="textWidth">
    /// How wide the text runs, in multiples of the glyph height — from <see cref="GlyphFont.MeasureWidth(int)"/>
    /// for the built-in lettering, or <see cref="LabelFontDefinition.MeasureWidth"/> for a captured font.
    /// </param>
    /// <returns>Center of the text, glyph height, heading of the text's "up" direction and the reading direction.</returns>
    public static (Vector2 Center, float Height, float UpHeadingDegrees, Vector2 ReadingDirection) LabelPlacementForWidth(Berth berth, float textWidth)
    {
        ArgumentNullException.ThrowIfNull(berth);
        var available = MathF.Max(0.5f, berth.Width * LabelWidthFraction);
        var height = Math.Clamp(available / MathF.Max(textWidth, 0.01f), MinLabelHeight, MaxLabelHeight);
        const float gap = 0.35f;
        var center = berth.Center - berth.Forward * (berth.Length * 0.5f + gap + height * 0.5f);
        var upHeading = berth.HeadingDegrees + 180f;
        // Glyph meshes read along their local −X, which maps to −Right(upHeading).
        return (center, height, upHeading, -MarinaMath.HeadingToRight(upHeading));
    }

    public static float AnimationPhase(string berthId) => MarinaMath.StableHash01(berthId) * MathF.Tau;

    /// <summary>
    /// Every boat drawn for the given berths: one per ordinary berth with a boat, and one per multi-berth.
    /// Hidden berths and berths excluded by <paramref name="filter"/> contribute nothing.
    /// </summary>
    /// <param name="berths">Berths to draw boats for.</param>
    /// <param name="berthLookup">Resolves a berth id, e.g. a multi-berth's members.</param>
    /// <param name="multiBerthLookup">Resolves a multi-berth id.</param>
    /// <param name="filter">Status filter.</param>
    /// <param name="groundHeight">Land height of a land berth (null for water berths); when null every boat floats.</param>
    /// <param name="meshes">Boat meshes, used to rest boats on land on their keels.</param>
    public static IEnumerable<BoatInstance> EnumerateBoats(
        IEnumerable<Berth> berths,
        Func<string, Berth?> berthLookup,
        Func<string, MultiBerth?> multiBerthLookup,
        BerthStatusFilter filter,
        Func<Berth, float?>? groundHeight = null,
        MeshLibrary? meshes = null)
    {
        HashSet<string>? emittedMultiBerths = null;
        foreach (var berth in berths)
        {
            if (!berth.IsVisible || !filter.Includes(berth.Status) || berth.Boat is null || !berth.Status.CanHaveBoat()) continue;

            if (berth.MultiBerthId is { } multiBerthId && multiBerthLookup(multiBerthId) is { } group)
            {
                emittedMultiBerths ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!emittedMultiBerths.Add(group.Id)) continue;

                var members = group.BerthIds.Select(berthLookup).OfType<Berth>().ToArray();
                if (members.Length == 0) continue;
                var groupGround = groundHeight?.Invoke(members[0]);
                yield return new BoatInstance(
                    group.Boat, group.Status, MultiBerthBoatWorld(members, group.Boat, group.Style, groupGround, meshes), members, group.Id, groupGround);
            }
            else
            {
                var ground = groundHeight?.Invoke(berth);
                yield return new BoatInstance(berth.Boat, berth.Status, BoatWorld(berth, berth.Boat, ground, meshes), new[] { berth }, null, ground);
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
