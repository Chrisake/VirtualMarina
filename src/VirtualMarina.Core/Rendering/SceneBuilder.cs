using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Geometry;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Picking;

namespace VirtualMarina.Core.Rendering;

/// <summary>Domain state the scene is built from.</summary>
internal sealed class SceneState
{
    public required IEnumerable<Pier> Piers { get; init; }

    public required IEnumerable<Berth> Berths { get; init; }

    public required IEnumerable<Divider> Dividers { get; init; }

    /// <summary>Adds preview objects (the designer's drawing) to the transparent pass.</summary>
    public Action<List<RenderObject>>? Overlay { get; init; }

    /// <summary>Land areas in layout order, each drawn with the mesh <see cref="LandMeshId"/> returns.</summary>
    public required IEnumerable<LandArea> Land { get; init; }

    public required Func<LandArea, int> LandMeshId { get; init; }

    public required Func<string, LandArea?> LandLookup { get; init; }

    public required Func<string, Berth?> BerthLookup { get; init; }

    public required Func<string, Pier?> PierLookup { get; init; }

    public required Func<string, MultiBerth?> MultiBerthLookup { get; init; }

    public required BerthStatusFilter Filter { get; init; }

    /// <summary>Selected berth ids (case-insensitive set).</summary>
    public required IReadOnlySet<string> Selected { get; init; }

    public required string? PrimarySelectedId { get; init; }

    public required string? HoveredBerthId { get; init; }

    public BerthLabelMode LabelMode { get; init; }

    public required MarinaStyle Style { get; init; }

    public StatusColorScheme Colors => Style.Status;

    public required MeshLibrary Meshes { get; init; }
}

/// <summary>Turns domain state into a flat, backend-neutral list of render objects.</summary>
internal static class SceneBuilder
{
    private static readonly Vector4 White = Vector4.One;
    private static readonly float[] Sides = { -1f, 1f };

    /// <summary>Sides (−1 left, +1 right) where boats berth and mooring points are drawn.</summary>
    private static IEnumerable<float> BerthSides(Pier pier) => Sides.Where(side => pier.HasBerthsOn(side < 0f ? PierSide.Left : PierSide.Right));

    // Pier and label colors come from the style (MarinaStyle.Piers / Labels); Build sets the palette for the current thread.
    [ThreadStatic]
    private static Palette? t_palette;

    private static Palette Colors => t_palette ??= new Palette(new MarinaStyle());

    private static Vector4 WoodDeck => Colors.WoodDeck;
    private static Vector4 WoodSeam => Colors.WoodSeam;
    private static Vector4 WoodWaler => Colors.WoodWaler;
    private static Vector4 WoodFinger => Colors.WoodFinger;
    private static Vector4 PontoonFloat => Colors.PontoonFloat;
    private static Vector4 ConcreteDeck => Colors.ConcreteDeck;
    private static Vector4 ConcreteCurb => Colors.ConcreteCurb;
    private static Vector4 ConcreteColumn => Colors.ConcreteColumn;
    private static Vector4 PontoonBody => Colors.PontoonBody;
    private static Vector4 PontoonTop => Colors.PontoonTop;
    private static Vector4 JointLine => Colors.JointLine;
    private static Vector4 Rubber => Colors.Rubber;
    private static Vector4 Steel => Colors.Steel;
    private static Vector4 Bollard => Colors.Bollard;
    private static Vector4 BoomFloat => Colors.BoomFloat;
    private static Vector4 BoomEnd => Colors.BoomEnd;
    private static Vector4 Pedestal => Colors.Pedestal;
    private static Vector4 PowerTop => Colors.PowerTop;
    private static Vector4 WaterTop => Colors.WaterTop;
    private static readonly Vector4 BoomLine = new(0.25f, 0.25f, 0.27f, 1f);

    // Land berths
    private static readonly Vector4 CradleSteel = new(0.22f, 0.32f, 0.52f, 1f);
    private static readonly Vector4 KeelBlock = new(0.45f, 0.33f, 0.22f, 1f);
    private static readonly Vector4 PostGray = new(0.55f, 0.56f, 0.58f, 1f);

    /// <summary>Clearance above the highest possible wave; the shader adds the wave bound (AboveWaves).</summary>
    public const float LabelHeightAboveWater = 0.04f;

    private const float FingerWidth = 0.7f;
    private const float FingerThickness = 0.25f;
    private const float PilingDepth = 2.5f;

    /// <summary>Height of the selection marker above the tallest thing in the berth (see <see cref="MarkerBaseHeight"/>).</summary>
    public const float MarkerClearance = 2.2f;

    /// <summary>Top of the selection marker (including bob) relative to its base.</summary>
    public const float MarkerTop = 4.3f;

    /// <summary>World Y of the selection marker's base.</summary>
    /// <param name="boatTop">World Y of the top of the berth's boat (or of the default clearance when it has none).</param>
    /// <param name="ground">Land height of a land berth; 0 on the water.</param>
    public static float MarkerBaseHeight(float boatTop, float ground = 0f) => MathF.Max(boatTop, ground + 2.5f) + MarkerClearance;

    /// <summary>Height of the land under a land berth, or null for a water berth (or a land berth whose land area is missing).</summary>
    public static float? GroundHeight(Berth berth, Func<string, LandArea?> landLookup) =>
        berth.LandAreaId is { } id && landLookup(id) is { } land ? land.Height : null;

    public static void Build(List<RenderObject> output, SceneState state)
    {
        output.Clear();
        var transparent = new List<RenderObject>();
        var colors = state.Colors;
        var selection = state.Style.Selection;
        t_palette = new Palette(state.Style);

        var berths = state.Berths as IReadOnlyCollection<Berth> ?? state.Berths.ToList();

        foreach (var land in state.Land) output.Add(new RenderObject(state.LandMeshId(land), Matrix4x4.Identity, White));
        foreach (var pier in state.Piers)
        {
            AddPier(output, pier);
            if (pier.Services != PierServices.None) AddServicePedestals(output, pier, berths);
        }

        foreach (var divider in state.Dividers) AddDivider(output, divider, divider.PierId is null ? null : state.PierLookup(divider.PierId));

        // Boats first, so the selection marker can sit above them.
        Func<Berth, float?> ground = berth => GroundHeight(berth, state.LandLookup);
        var boatTops = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var boat in BerthPlacement.EnumerateBoats(berths, state.BerthLookup, state.MultiBerthLookup, state.Filter, ground, state.Meshes))
        {
            var top = BerthPlacement.BoatTopHeight(boat.Boat, state.Meshes, boat.Ground);
            foreach (var member in boat.Berths) boatTops[member.Id] = top;
            AddBoat(output, transparent, boat, state);
        }

        foreach (var berth in berths)
        {
            // Hidden berths draw nothing at all, not even their physical structure.
            if (!berth.IsVisible) continue;

            var berthGround = ground(berth);
            if (berth.HasFingerPiers && !berth.IsOnLand) AddFingerPiers(output, berth, berth.PierId is null ? null : state.PierLookup(berth.PierId), state);

            // The status filter hides status visuals and boats, but not structure.
            if (!state.Filter.Includes(berth.Status)) continue;

            var isSelected = state.Selected.Contains(berth.Id);
            var isHovered = !isSelected && berth.IsInteractive && string.Equals(berth.Id, state.HoveredBerthId, StringComparison.OrdinalIgnoreCase);
            var statusColor = berth.IsDisabled ? colors.DisabledColor : colors.Get(berth.Status);
            var phase = BerthPlacement.AnimationPhase(berth.Id);
            var desaturation = berth.IsDisabled ? 1f : 0f;

            // Status pad on the water.
            var padAlpha = isSelected ? MathF.Max(colors.PadOpacity, 0.72f) : isHovered ? MathF.Max(colors.PadOpacity, 0.6f) : berth.IsDisabled ? colors.PadOpacity * 0.75f : colors.PadOpacity;
            var padEmissive = isSelected ? selection.SelectedGlow : isHovered ? selection.HoverGlow : 0.05f;
            if (padAlpha > 0.005f)
            {
                transparent.Add(new RenderObject(
                    MeshIds.BerthPad, BerthPlacement.PadWorld(berth, berthGround), statusColor.WithAlpha(padAlpha).ToVector4(), padEmissive,
                    isSelected && selection.Pulse ? RenderAnimation.Pulse : RenderAnimation.None, phase, desaturation));
            }

            var markerScale = colors.StatusMarkerScale;
            if (!colors.ShowStatusMarkers)
            {
                // No buoy or post.
            }
            else if (berthGround is { } landHeight)
            {
                // Status post at the rear of a land berth, visible from far away.
                var post = BerthPlacement.StatusPostPosition(berth);
                output.Add(Cylinder(post, landHeight, new Vector3(0.12f * markerScale, BerthPlacement.StatusPostHeight * markerScale, 0.12f * markerScale), PostGray) with { Desaturation = desaturation });
                output.Add(new RenderObject(
                    MeshIds.Buoy,
                    Matrix4x4.CreateScale(0.7f * markerScale) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(post, landHeight + (BerthPlacement.StatusPostHeight + 0.3f) * markerScale)),
                    statusColor.WithAlpha(1f).ToVector4(), berth.IsDisabled ? 0.05f : 0.35f, RenderAnimation.None, phase, desaturation));
            }
            else
            {
                // Status buoy, visible from far away.
                output.Add(new RenderObject(
                    MeshIds.Buoy,
                    Matrix4x4.CreateScale(0.9f * markerScale) * Matrix4x4.CreateTranslation(BerthPlacement.BuoyPosition(berth)),
                    statusColor.WithAlpha(1f).ToVector4(), berth.IsDisabled ? 0.05f : 0.35f, RenderAnimation.FloatOnWater, phase, desaturation));
            }

            if (state.LabelMode.Includes(berth.Status)) AddLabel(output, berth, isSelected || isHovered, phase, berthGround);

            if (isSelected && selection.ShowMarker)
            {
                var isPrimary = string.Equals(berth.Id, state.PrimarySelectedId, StringComparison.OrdinalIgnoreCase);
                var baseGround = berthGround ?? 0f;
                var boatTop = boatTops.TryGetValue(berth.Id, out var t) ? t : baseGround + 3f;
                var markerPosition = MarinaMath.ToWorld(berth.Center, MarkerBaseHeight(boatTop, baseGround));
                output.Add(new RenderObject(
                    MeshIds.SelectionMarker,
                    Matrix4x4.CreateScale((isPrimary ? 2.4f : 1.7f) * selection.MarkerScale) * Matrix4x4.CreateTranslation(markerPosition),
                    selection.MarkerTint.WithAlpha(1f).ToVector4(), 0.45f, RenderAnimation.SpinAndBob, isPrimary ? 0f : phase));
            }
        }

        state.Overlay?.Invoke(transparent);
        output.AddRange(transparent);
    }

    /// <summary>
    /// The berth's name written flat on the water past its open end, one object per character. The characters sit just
    /// above the highest wave the water can reach (see <see cref="RenderAnimation.AboveWaves"/>), so waves never hide them.
    /// Land berths get their name on the ground, just above their pad.
    /// </summary>
    private static void AddLabel(List<RenderObject> output, Berth berth, bool highlighted, float phase, float? ground)
    {
        var text = berth.DisplayName.Trim();
        if (text.Length == 0) return;

        var (center, height, upHeading, reading) = BerthPlacement.LabelPlacement(berth, text.Length);
        var start = center - reading * (GlyphFont.MeasureWidth(text.Length) * height * 0.5f - GlyphFont.GlyphWidth * height * 0.5f);
        var tint = berth.IsDisabled ? Colors.LabelDisabled : highlighted ? Colors.LabelHighlight : Colors.Label;
        var scale = new Vector3(height, 1f, height);

        for (var i = 0; i < text.Length; i++)
        {
            if (!GlyphFont.TryGetMeshId(text[i], out var meshId)) continue;
            var position = start + reading * (i * GlyphFont.Advance * height);
            output.Add(new RenderObject(
                meshId,
                MarinaMath.CreatePlacement(scale, upHeading, MarinaMath.ToWorld(position, ground is { } g ? g + BerthPlacement.LandPadLift + 0.02f : LabelHeightAboveWater)),
                tint, highlighted ? 0.35f : 0.15f, ground.HasValue ? RenderAnimation.None : RenderAnimation.AboveWaves, phase));
        }
    }

    private static void AddBoat(List<RenderObject> output, List<RenderObject> transparent, BoatInstance boat, SceneState state)
    {
        var colors = state.Colors;
        var selection = state.Style.Selection;
        var isSelected = boat.Berths.Any(s => state.Selected.Contains(s.Id));
        var isHovered = !isSelected && state.HoveredBerthId is { } hovered &&
            boat.Berths.Any(s => s.IsInteractive && string.Equals(s.Id, hovered, StringComparison.OrdinalIgnoreCase));
        var disabled = boat.IsDisabled;
        var animation = (boat.OnLand ? RenderAnimation.None : RenderAnimation.FloatOnWater) | (isSelected && selection.Pulse ? RenderAnimation.Pulse : RenderAnimation.None);
        var emissive = isSelected ? selection.SelectedGlow * 0.67f : isHovered ? selection.HoverGlow * 0.6f : 0f;
        var phase = BerthPlacement.AnimationPhase(boat.MultiBerthId ?? boat.PrimaryBerth.Id);
        var meshId = MeshIds.ForBoat(boat.Boat.Type);
        var opacity = colors.GetBoatOpacity(boat.Status);
        if (opacity <= 0.005f) return;

        if (!boat.Status.ShowsGhostBoat())
        {
            if (boat.Ground is { } ground) AddCradle(output, boat, ground, state.Meshes, disabled ? 1f : 0f);
            var tint = (disabled ? new Vector4(0.92f, 0.92f, 0.92f, 1f) : White) with { W = opacity };
            (opacity >= 0.999f ? output : transparent).Add(new RenderObject(meshId, boat.World, tint, emissive, animation, phase, disabled ? 1f : 0f));
            return;
        }

        // Reserved / temporarily free: a "ghost" of the boat, tinted toward the status color.
        var statusColor = disabled ? colors.DisabledColor : colors.Get(boat.Status);
        var ghost = new ColorRgba(1f, 1f, 1f).Lerp(statusColor, colors.GhostBoatTint).WithAlpha(opacity);
        (opacity >= 0.999f ? output : transparent).Add(new RenderObject(
            meshId, boat.World, ghost.ToVector4(), disabled ? emissive : MathF.Max(emissive, 0.25f), animation, phase, disabled ? 1f : 0f));
    }

    /// <summary>Keel blocks and side supports holding a boat stored ashore.</summary>
    private static void AddCradle(List<RenderObject> output, BoatInstance boat, float ground, MeshLibrary meshes, float desaturation)
    {
        var world = boat.World;
        var origin = MarinaMath.ToPlan(world.Translation);
        var forward = MarinaMath.ToPlan(new Vector3(world.M31, world.M32, world.M33));
        var right = MarinaMath.ToPlan(new Vector3(world.M11, world.M12, world.M13));
        if (forward.LengthSquared() < 1e-8f || right.LengthSquared() < 1e-8f) return;
        forward = Vector2.Normalize(forward);
        right = Vector2.Normalize(right);
        var heading = MarinaMath.DirectionToHeading(forward);

        var length = boat.Boat.LengthMeters;
        var beam = boat.Boat.BeamMeters;
        var keel = ground + BerthPlacement.CradleHeight;
        var hullSide = keel + BerthPlacement.KeelDepth(boat.Boat, meshes) * 0.8f;

        foreach (var along in new[] { -0.28f, 0.22f })
        {
            var at = origin + forward * (length * along);
            output.Add(new RenderObject(
                MeshIds.UnitBox,
                MarinaMath.CreatePlacement(new Vector3(0.5f, BerthPlacement.CradleHeight, 0.7f), heading, MarinaMath.ToWorld(at, ground + BerthPlacement.CradleHeight * 0.5f)),
                KeelBlock, 0f, RenderAnimation.None, 0f, desaturation));

            foreach (var side in Sides)
            {
                var foot = at + right * (side * MathF.Max(0.5f, beam * 0.42f));
                output.Add(Cylinder(foot, ground, new Vector3(0.12f, hullSide - ground, 0.12f), CradleSteel) with { Desaturation = desaturation });
                output.Add(new RenderObject(
                    MeshIds.UnitBox,
                    MarinaMath.CreatePlacement(new Vector3(0.7f, 0.06f, 0.7f), heading, MarinaMath.ToWorld(foot, ground + 0.03f)),
                    CradleSteel, 0f, RenderAnimation.None, 0f, desaturation));
            }
        }
    }

    // ---- Piers --------------------------------------------------------------------------------------

    private static void AddPier(List<RenderObject> output, Pier pier)
    {
        switch (pier.Type)
        {
            case PierType.Concrete: AddConcretePier(output, pier); break;
            case PierType.FloatingConcrete: AddFloatingConcretePier(output, pier); break;
            default: AddFloatingWoodenPier(output, pier); break;
        }
    }

    /// <summary>Fixed pier: thick slab on square columns, curbs along the edges, bollards.</summary>
    private static void AddConcretePier(List<RenderObject> output, Pier pier)
    {
        const float slab = 0.45f;
        var top = pier.DeckHeight;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, slab, pier.Length), top - slab * 0.5f, ConcreteDeck));

        foreach (var side in Sides)
        {
            var curbOffset = pier.Right * side * (pier.Width * 0.5f - 0.14f);
            output.Add(Box(pier, pier.Center + curbOffset, 0f, new Vector3(0.28f, 0.16f, pier.Length), top + 0.08f, ConcreteCurb));
        }

        var count = Math.Max(2, (int)MathF.Floor(pier.Length / pier.PilingSpacing) + 1);
        for (var i = 0; i < count; i++)
        {
            var along = 0.4f + (pier.Length - 0.8f) * i / (count - 1);
            foreach (var side in Sides)
            {
                var column = pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.35f);
                var height = top - slab + PilingDepth;
                output.Add(Box(pier, column, 0f, new Vector3(0.5f, height, 0.5f), top - slab - height * 0.5f, ConcreteColumn));

                if (i % 2 == 1 && pier.HasBerthsOn(side < 0f ? PierSide.Left : PierSide.Right))
                {
                    var bollard = pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.55f);
                    output.Add(Cylinder(bollard, top, new Vector3(0.32f, 0.38f, 0.32f), Bollard));
                }
            }
        }
    }

    /// <summary>Plank deck with walers on dark pontoon floats.</summary>
    private static void AddFloatingWoodenPier(List<RenderObject> output, Pier pier)
    {
        const float deck = 0.14f;
        var top = pier.DeckHeight;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, deck, pier.Length), top - deck * 0.5f, WoodDeck));

        // Plank seams.
        const float plankPitch = 1.2f;
        for (var along = plankPitch; along < pier.Length - 0.1f; along += plankPitch)
        {
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width, 0.012f, 0.05f), top + 0.004f, WoodSeam));
        }

        foreach (var side in Sides)
        {
            var waler = pier.Right * side * (pier.Width * 0.5f + 0.09f);
            output.Add(Box(pier, pier.Center + waler, 0f, new Vector3(0.18f, 0.3f, pier.Length), top - 0.17f, WoodWaler));
        }

        AddPontoonFloats(output, pier, top - deck);
    }

    /// <summary>Monolithic concrete pontoon with rubber fenders, section joints and cleats.</summary>
    private static void AddFloatingConcretePier(List<RenderObject> output, Pier pier)
    {
        const float bottom = -0.35f;
        var top = pier.DeckHeight;
        var height = top - bottom;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, height - 0.04f, pier.Length), bottom + (height - 0.04f) * 0.5f, PontoonBody));
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width - 0.16f, 0.04f, pier.Length - 0.16f), top - 0.02f, PontoonTop));

        foreach (var side in BerthSides(pier))
        {
            var fender = pier.Right * side * (pier.Width * 0.5f + 0.06f);
            output.Add(Box(pier, pier.Center + fender, 0f, new Vector3(0.12f, 0.22f, pier.Length), top - 0.16f, Rubber));
        }

        const float sectionLength = 12f;
        for (var along = sectionLength; along < pier.Length - 0.5f; along += sectionLength)
        {
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width - 0.1f, 0.012f, 0.04f), top + 0.004f, JointLine));
        }

        var cleats = Math.Max(2, (int)MathF.Floor(pier.Length / pier.PilingSpacing) + 1);
        for (var i = 0; i < cleats; i++)
        {
            var along = 0.6f + (pier.Length - 1.2f) * i / (cleats - 1);
            foreach (var side in BerthSides(pier))
            {
                output.Add(Cylinder(pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.3f), top, new Vector3(0.22f, 0.22f, 0.22f), Bollard));
            }
        }

    }

    /// <summary>
    /// Power and water pedestals (<see cref="Pier.Services"/>): one for every two berths on a berthing side, standing between them,
    /// so each berth has exactly one within reach. A lone berth gets one at the middle of its frontage, and a stretch of pier without
    /// berths gets none.
    /// </summary>
    private static void AddServicePedestals(List<RenderObject> output, Pier pier, IEnumerable<Berth> berths)
    {
        const float postHeight = 0.95f;
        var top = pier.DeckHeight;
        var power = (pier.Services & PierServices.Power) != 0;
        var both = pier.Services == PierServices.PowerAndWater;

        foreach (var side in BerthSides(pier))
        {
            var offset = pier.Right * side * MathF.Max(0.12f, pier.Width * 0.5f - 0.3f);
            var row = BerthSpansAlong(pier, berths, side);
            for (var i = 0; i < row.Count; i += 2)
            {
                // Between the two berths of a pair, or halfway along a berth left on its own at the end of the row.
                var along = i + 1 < row.Count ? (row[i].Max + row[i + 1].Min) * 0.5f : (row[i].Min + row[i].Max) * 0.5f;
                var at = pier.Start + pier.Direction * Math.Clamp(along, 0.25f, MathF.Max(0.25f, pier.Length - 0.25f)) + offset;
                output.Add(Box(pier, at, 0f, new Vector3(0.28f, postHeight, 0.28f), top + postHeight * 0.5f, Pedestal));
                output.Add(Box(pier, at, 0f, new Vector3(0.34f, 0.1f, 0.34f), top + postHeight + 0.05f, power ? PowerTop : WaterTop));
                if (both) output.Add(Box(pier, at, 0f, new Vector3(0.3f, 0.14f, 0.3f), top + postHeight * 0.45f, WaterTop));
            }
        }
    }

    /// <summary>
    /// The stretch each berth on one side of a pier covers along it (distance from the pier's start to its near and far edges), in
    /// order along the pier.
    /// </summary>
    internal static IReadOnlyList<(float Min, float Max)> BerthSpansAlong(Pier pier, IEnumerable<Berth> berths, float side)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentNullException.ThrowIfNull(berths);
        var row = new List<(float Min, float Max)>();
        foreach (var berth in berths)
        {
            if (!berth.IsVisible || !string.Equals(berth.PierId, pier.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (Vector2.Dot(berth.Center - pier.Center, pier.Right) * side <= 0f) continue;

            var along = Vector2.Dot(berth.Center - pier.Start, pier.Direction);
            var extent = MathF.Abs(Vector2.Dot(berth.Right, pier.Direction)) * berth.Width * 0.5f +
                         MathF.Abs(Vector2.Dot(berth.Forward, pier.Direction)) * berth.Length * 0.5f;
            row.Add((along - extent, along + extent));
        }

        row.Sort((a, b) => a.Min.CompareTo(b.Min));
        return row;
    }

    private static void AddPontoonFloats(List<RenderObject> output, Pier pier, float underside)
    {
        const float bottom = -0.35f;
        const float segment = 4f;
        const float pitch = 4.6f;
        var height = underside - bottom;
        if (height <= 0.02f) return;

        var count = Math.Max(1, (int)MathF.Floor((pier.Length - 0.4f) / pitch));
        var used = count * pitch - (pitch - segment);
        var start = (pier.Length - used) * 0.5f;
        for (var i = 0; i < count; i++)
        {
            var along = start + i * pitch + segment * 0.5f;
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width * 0.85f, height, segment), bottom + height * 0.5f, PontoonFloat));
        }
    }

    // ---- Finger piers and dividers ----------------------------------------------------------------

    private static void AddFingerPiers(List<RenderObject> output, Berth berth, Pier? pier, SceneState state)
    {
        var deckHeight = pier?.DeckHeight ?? 0.6f;
        var pierType = pier?.Type ?? PierType.FloatingWooden;
        var group = berth.MultiBerthId is { } multiBerthId ? state.MultiBerthLookup(multiBerthId) : null;
        var fingerLength = berth.Length * 0.75f;
        var y = MathF.Min(deckHeight, 0.6f) - 0.12f - FingerThickness * 0.5f;

        foreach (var side in Sides)
        {
            // A boat spanning several berths lies across the fingers between them, so those aren't drawn.
            if (group is not null && SharesEdgeWithBerthMember(berth, side, group, state)) continue;

            var edge = berth.Center + berth.Right * side * (berth.Width * 0.5f);
            var center = edge + berth.Forward * (berth.Length * 0.5f - fingerLength * 0.5f);
            output.Add(new RenderObject(
                MeshIds.UnitBox,
                MarinaMath.CreatePlacement(new Vector3(FingerWidth, FingerThickness, fingerLength), berth.HeadingDegrees, MarinaMath.ToWorld(center, y)),
                pierType == PierType.FloatingWooden ? WoodFinger : PontoonTop));

            var outerEnd = edge + berth.Forward * (berth.Length * 0.5f - fingerLength);
            output.Add(pierType == PierType.FloatingWooden ? Piling(outerEnd, y + 1.2f, 0.32f) : SteelPile(outerEnd, y + 1.4f, 0.36f));
        }
    }

    private static bool SharesEdgeWithBerthMember(Berth berth, float side, MultiBerth multiBerth, SceneState state)
    {
        var probe = berth.Center + berth.Right * side * (berth.Width * 0.5f + 0.35f);
        foreach (var id in multiBerth.BerthIds)
        {
            if (string.Equals(id, berth.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (state.BerthLookup(id) is { IsVisible: true } other && other.Bounds.Contains(probe)) return true;
        }

        return false;
    }

    private static void AddDivider(List<RenderObject> output, Divider divider, Pier? pier)
    {
        var deckHeight = pier?.DeckHeight ?? 0.5f;
        switch (divider.Type)
        {
            case DividerType.Piles:
            {
                var count = Math.Max(2, (int)MathF.Floor(divider.Length / divider.Spacing) + 1);
                var steel = pier is { Type: not PierType.FloatingWooden };
                for (var i = 0; i < count; i++)
                {
                    var position = divider.Start + divider.Direction * (divider.Length * i / (count - 1));
                    output.Add(steel ? SteelPile(position, deckHeight + 1.4f, divider.Width) : Piling(position, deckHeight + 1.4f, divider.Width));
                }

                break;
            }

            case DividerType.Boom:
            {
                const float y = 0.12f;
                output.Add(new RenderObject(
                    MeshIds.UnitBox,
                    MarinaMath.CreatePlacement(new Vector3(0.08f, 0.06f, divider.Length), divider.HeadingDegrees, MarinaMath.ToWorld(divider.Center, y)),
                    BoomLine));
                var count = Math.Max(2, (int)MathF.Floor(divider.Length / divider.Spacing) + 1);
                for (var i = 0; i < count; i++)
                {
                    var isEnd = i == 0 || i == count - 1;
                    var position = divider.Start + divider.Direction * (divider.Length * i / (count - 1));
                    var size = divider.Width * (isEnd ? 1.5f : 1f);
                    output.Add(new RenderObject(
                        MeshIds.Buoy,
                        Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, y)),
                        isEnd ? BoomEnd : BoomFloat, 0.1f, RenderAnimation.FloatOnWater, MarinaMath.StableHash01(divider.Id) * MathF.Tau + i * 0.7f));
                }

                break;
            }

            case DividerType.SinglePile:
            {
                // Mediterranean mooring: one pile at the outer end of the boundary, nothing in between.
                var steel = pier is { Type: not PierType.FloatingWooden };
                output.Add(steel ? SteelPile(divider.End, deckHeight + 1.6f, divider.Width) : Piling(divider.End, deckHeight + 1.4f, divider.Width));
                break;
            }

            default:
            {
                var wooden = pier is null or { Type: PierType.FloatingWooden };
                var y = MathF.Min(deckHeight, 0.6f) - 0.12f - FingerThickness * 0.5f;
                output.Add(new RenderObject(
                    MeshIds.UnitBox,
                    MarinaMath.CreatePlacement(new Vector3(divider.Width, FingerThickness, divider.Length), divider.HeadingDegrees, MarinaMath.ToWorld(divider.Center, y)),
                    wooden ? WoodFinger : PontoonTop));
                output.Add(wooden ? Piling(divider.End, y + 1.2f, 0.34f) : SteelPile(divider.End, y + 1.4f, 0.38f));
                break;
            }
        }
    }

    // ---- Primitives --------------------------------------------------------------------------------

    /// <summary>Box aligned with the pier's heading, centered at <paramref name="plan"/> and <paramref name="centerY"/>.</summary>
    private static RenderObject Box(Pier pier, Vector2 plan, float headingOffset, Vector3 size, float centerY, Vector4 color) =>
        new(MeshIds.UnitBox, MarinaMath.CreatePlacement(size, pier.HeadingDegrees + headingOffset, MarinaMath.ToWorld(plan, centerY)), color);

    private static RenderObject Cylinder(Vector2 plan, float baseY, Vector3 size, Vector4 color) =>
        new(MeshIds.Cylinder, Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(plan, baseY)), color);

    private static RenderObject Piling(Vector2 position, float topHeight, float diameter = 0.32f) =>
        new(MeshIds.Piling,
            Matrix4x4.CreateScale(diameter, topHeight + PilingDepth, diameter) *
            Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, -PilingDepth)),
            Vector4.One);

    /// <summary>Pier, boom and label colors for one scene build, derived from the style.</summary>
    private sealed class Palette
    {
        public Palette(MarinaStyle style)
        {
            var d = style.Piers;
            var wood = d.WoodColor.ToVector3();
            var concrete = d.ConcreteColor.ToVector3();
            WoodDeck = Opaque(wood);
            WoodSeam = Opaque(wood * 0.64f);
            WoodWaler = Opaque(wood * 0.75f);
            WoodFinger = Opaque(wood * 1.07f);
            PontoonFloat = Opaque(d.FloatColor.ToVector3());
            ConcreteDeck = Opaque(concrete);
            ConcreteCurb = Opaque(concrete * 1.135f);
            ConcreteColumn = Opaque(concrete * 0.785f);
            PontoonBody = Opaque(concrete * 0.92f);
            PontoonTop = Opaque(concrete * 1.12f);
            JointLine = Opaque(concrete * 0.61f);
            Rubber = Opaque(d.FenderColor.ToVector3());
            Steel = Opaque(d.SteelColor.ToVector3());
            Bollard = Opaque(d.BollardColor.ToVector3());
            BoomFloat = Opaque(d.BoomFloatColor.ToVector3());
            BoomEnd = Opaque(d.BoomEndColor.ToVector3());
            Pedestal = Opaque(d.PedestalColor.ToVector3());
            PowerTop = Opaque(d.PowerColor.ToVector3());
            WaterTop = Opaque(d.WaterColor.ToVector3());
            Label = Opaque(style.Labels.Color.ToVector3());
            LabelHighlight = Opaque(style.Labels.HighlightColor.ToVector3());
            LabelDisabled = Opaque(style.Labels.DisabledColor.ToVector3());
        }

        public Vector4 WoodDeck { get; }
        public Vector4 WoodSeam { get; }
        public Vector4 WoodWaler { get; }
        public Vector4 WoodFinger { get; }
        public Vector4 PontoonFloat { get; }
        public Vector4 ConcreteDeck { get; }
        public Vector4 ConcreteCurb { get; }
        public Vector4 ConcreteColumn { get; }
        public Vector4 PontoonBody { get; }
        public Vector4 PontoonTop { get; }
        public Vector4 JointLine { get; }
        public Vector4 Rubber { get; }
        public Vector4 Steel { get; }
        public Vector4 Bollard { get; }
        public Vector4 BoomFloat { get; }
        public Vector4 BoomEnd { get; }
        public Vector4 Pedestal { get; }
        public Vector4 PowerTop { get; }
        public Vector4 WaterTop { get; }
        public Vector4 Label { get; }
        public Vector4 LabelHighlight { get; }
        public Vector4 LabelDisabled { get; }

        private static Vector4 Opaque(Vector3 color) => new(Vector3.Clamp(color, Vector3.Zero, Vector3.One), 1f);
    }

    private static RenderObject SteelPile(Vector2 position, float topHeight, float diameter) =>
        new(MeshIds.Cylinder,
            Matrix4x4.CreateScale(diameter, topHeight + PilingDepth, diameter) *
            Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, -PilingDepth)),
            Steel);
}
