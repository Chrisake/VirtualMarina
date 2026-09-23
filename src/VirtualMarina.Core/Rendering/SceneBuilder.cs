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

    public required IReadOnlyList<Berth> Berths { get; init; }

    public required IEnumerable<Divider> Dividers { get; init; }

    /// <summary>Land areas in layout order, each drawn with the mesh <see cref="LandMeshId"/> returns.</summary>
    public required IEnumerable<LandArea> Land { get; init; }

    public required Func<LandArea, int> LandMeshId { get; init; }

    /// <summary>What stands on a land area — its trees, or a breakwater's rocks — as instances drawn over its ground.</summary>
    public required Func<LandArea, IReadOnlyList<RenderObject>> LandScenery { get; init; }

    /// <summary>What stands on the mainland: trees, fields, a town.</summary>
    public IReadOnlyList<RenderObject> ShorelineScenery { get; init; } = [];

    /// <summary>World Y of the mainland's surface, which its scenery casts its shadow on.</summary>
    public float ShorelineGroundHeight { get; init; }

    /// <summary>True when there is a mainland to draw, beneath every land area.</summary>
    public required bool HasShoreline { get; init; }

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

/// <summary>Something that casts a shadow, and the height of the ground the shadow lands on.</summary>
internal readonly record struct ShadowCaster(RenderObject Caster, float Ground);

/// <summary>
/// The berths layer as built: its objects in the order they were made, and which of them belong to which berth or boat,
/// so a change of hover or selection can rewrite just those in place.
/// </summary>
internal sealed class BerthLayerContent
{
    public List<RenderObject> Objects { get; } = [];

    /// <summary>One entry per berth or boat: where its objects start in <see cref="Objects"/> and how many there are.</summary>
    public List<(int Start, int Count, Berth? Berth, BoatInstance? Boat)> Units { get; } = [];

    /// <summary>For each berth, the units drawn differently when it is hovered or selected: its own, and its boat's.</summary>
    public Dictionary<string, List<int>> UnitsByBerth { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>World Y of the top of each berth's boat, which the selection marker stands above.</summary>
    public Dictionary<string, float> BoatTops { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        Objects.Clear();
        Units.Clear();
        UnitsByBerth.Clear();
        BoatTops.Clear();
    }

    public void AddUnit(int start, Berth? berth, BoatInstance? boat, IEnumerable<Berth> members)
    {
        var unit = Units.Count;
        Units.Add((start, Objects.Count - start, berth, boat));
        foreach (var member in members)
        {
            if (!UnitsByBerth.TryGetValue(member.Id, out var list)) UnitsByBerth[member.Id] = list = [];
            list.Add(unit);
        }
    }
}

/// <summary>
/// Turns domain state into backend-neutral render objects, one layer at a time (see <see cref="RenderLayerKind"/>).
/// </summary>
/// <remarks>
/// The colors of one build come from its style, through a <see cref="Palette"/> handed down explicitly, so building on
/// several threads, or for several visualizers, never mixes their colors.
/// </remarks>
internal static class SceneBuilder
{
    private static readonly Vector4 White = Vector4.One;
    private static readonly float[] Sides = { -1f, 1f };

    /// <summary>Sides (−1 left, +1 right) where boats berth and mooring points are drawn.</summary>
    private static IEnumerable<float> BerthSides(Pier pier) => Sides.Where(side => pier.HasBerthsOn(side < 0f ? PierSide.Left : PierSide.Right));

    private static readonly Vector4 BoomLine = new(0.25f, 0.25f, 0.27f, 1f);

    // Land berths
    private static readonly Vector4 CradleSteel = new(0.22f, 0.32f, 0.52f, 1f);
    private static readonly Vector4 KeelBlock = new(0.45f, 0.33f, 0.22f, 1f);
    private static readonly Vector4 PostGray = new(0.55f, 0.56f, 0.58f, 1f);

    /// <summary>Clearance above the highest possible wave; the shader adds the wave bound (AboveWaves).</summary>
    public const float LabelHeightAboveWater = 0.04f;

    /// <summary>
    /// How far a land berth's label floats above the ground it stands on, in meters. A label lying on the surface is
    /// all but invisible from a low camera, so it is lifted clear of the land and its status pad.
    /// </summary>
    public const float LabelHeightAboveLand = 0.45f;

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

    /// <summary>
    /// How far a shadow floats above the ground it lands on, in meters. Enough to stay off the surface it is drawn
    /// over without reading as a gap.
    /// </summary>
    private const float ShadowLift = 0.03f;

    /// <summary>True when shadows are drawn at all for this style and sun.</summary>
    public static bool CastsShadows(MarinaStyle style) =>
        style.Shadows.IsEnabled && style.Shadows.Strength > 0.004f && ShadowProjection.CanCast(style.Lighting.SunDirection);

    /// <summary>
    /// The shadows of <paramref name="casters"/>: the same geometry squashed onto the ground along the sun's rays and drawn
    /// dark and unlit.
    /// </summary>
    /// <remarks>
    /// The floating animation is carried over, so a boat's shadow rides the same wave the boat does instead of staying flat
    /// while the water under it moves.
    /// </remarks>
    public static void CastShadows(List<RenderObject> output, IReadOnlyList<ShadowCaster> casters, Vector3 sun, float strength)
    {
        output.Clear();
        var tint = new Vector4(0f, 0f, 0f, strength);
        var flatten = default(Matrix4x4);
        var flattenGround = float.NaN;
        foreach (var (caster, ground) in casters)
        {
            // Most casters stand on one of a few grounds (the water, a quay), so the projection is worked out once per run.
            if (ground != flattenGround)
            {
                flatten = ShadowProjection.OntoPlane(sun, ground + ShadowLift);
                flattenGround = ground;
            }

            output.Add(caster with
            {
                World = caster.World * flatten,
                Tint = tint,
                Emissive = 0f,
                Desaturation = 0f,
                Animation = (caster.Animation & ~RenderAnimation.Pulse) | RenderAnimation.Unlit,
            });
        }
    }

    /// <summary>
    /// True when replacing <paramref name="before"/> with <paramref name="after"/> changes the structure layer (fingers,
    /// pedestals) and not only what the berths layer shows: its place, size, visibility, fingers, services or multi-berth.
    /// </summary>
    public static bool ShapesStructure(Berth before, Berth after) =>
        !string.Equals(before.PierId, after.PierId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(before.LandAreaId, after.LandAreaId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(before.MultiBerthId, after.MultiBerthId, StringComparison.OrdinalIgnoreCase) ||
        before.Center != after.Center || before.HeadingDegrees != after.HeadingDegrees ||
        before.Length != after.Length || before.Width != after.Width ||
        before.HasFingerPiers != after.HasFingerPiers || before.IsVisible != after.IsVisible || before.Services != after.Services;

    /// <summary>True when a mesh is registered under this id and has anything in it to draw.</summary>
    private static bool HasGeometry(MeshLibrary meshes, int id) =>
        meshes.TryGet(id, out var mesh) && mesh.Indices.Length > 0;

    /// <summary>Height of the land under a land berth, or null for a water berth (or a land berth whose land area is missing).</summary>
    public static float? GroundHeight(Berth berth, Func<string, LandArea?> landLookup) =>
        berth.LandAreaId is { } id && landLookup(id) is { } land ? land.Height : null;

    // ---- Structure ----------------------------------------------------------------------------------

    /// <summary>
    /// The layout itself: mainland, land areas and what stands on them, piers with their pedestals, dividers and finger
    /// piers. Everything that casts a shadow is added to <paramref name="casters"/>.
    /// </summary>
    public static void BuildStructure(List<RenderObject> output, List<ShadowCaster> casters, SceneState state)
    {
        output.Clear();
        casters.Clear();
        var palette = new Palette(state.Style);

        // The mainland goes down first, so the land areas traced along the shore sit on top of it.
        if (state.HasShoreline)
        {
            output.Add(new RenderObject(MeshIds.Shoreline, Matrix4x4.Identity, White));
            foreach (var scenery in state.ShorelineScenery)
            {
                output.Add(scenery);
                casters.Add(new ShadowCaster(scenery, state.ShorelineGroundHeight));
            }
        }

        foreach (var land in state.Land)
        {
            var ground = state.LandMeshId(land);
            if (HasGeometry(state.Meshes, ground)) output.Add(new RenderObject(ground, Matrix4x4.Identity, White));

            // Trees and rocks stand on their land area, so that is the ground their shadow falls on.
            foreach (var scenery in state.LandScenery(land))
            {
                output.Add(scenery);
                casters.Add(new ShadowCaster(scenery, land.Height));
            }
        }

        // The berths of each pier, gathered once rather than searched for pier by pier.
        var byPier = new Dictionary<string, List<Berth>>(StringComparer.OrdinalIgnoreCase);
        foreach (var berth in state.Berths)
        {
            if (!berth.IsVisible || berth.PierId is not { } pierId) continue;
            if (!byPier.TryGetValue(pierId, out var list)) byPier[pierId] = list = [];
            list.Add(berth);
        }

        // Piers and what stands on them are over water, so their shadows land on it.
        var structureFrom = output.Count;
        foreach (var pier in state.Piers)
        {
            AddPier(output, pier, palette);
            var berths = byPier.TryGetValue(pier.Id, out var onPier) ? onPier : [];
            if (pier.Services != PierServices.None || berths.Exists(berth => berth.Services is { } own && own != PierServices.None))
            {
                AddServicePedestals(output, pier, berths, palette);
            }
        }

        foreach (var divider in state.Dividers) AddDivider(output, divider, divider.PierId is null ? null : state.PierLookup(divider.PierId), palette);

        foreach (var berth in state.Berths)
        {
            // Hidden berths draw nothing at all, not even their physical structure; the status filter hides status
            // visuals and boats, but not structure.
            if (!berth.IsVisible || !berth.HasFingerPiers || berth.IsOnLand) continue;
            AddFingerPiers(output, berth, berth.PierId is null ? null : state.PierLookup(berth.PierId), state, palette);
        }

        for (var i = structureFrom; i < output.Count; i++) casters.Add(new ShadowCaster(output[i], 0f));
    }

    // ---- Berths -------------------------------------------------------------------------------------

    /// <summary>
    /// What the berths' statuses show: boats (with the cradles of boats ashore), status pads, buoys or posts, and labels.
    /// The boats are added to <paramref name="casters"/>.
    /// </summary>
    public static void BuildBerths(BerthLayerContent content, List<ShadowCaster> casters, SceneState state)
    {
        content.Clear();
        casters.Clear();
        var palette = new Palette(state.Style);
        Func<Berth, float?> ground = berth => GroundHeight(berth, state.LandLookup);

        // Boats first, so the selection marker can sit above them.
        foreach (var boat in BerthPlacement.EnumerateBoats(state.Berths, state.BerthLookup, state.MultiBerthLookup, state.Filter, ground, state.Meshes))
        {
            var top = BerthPlacement.BoatTopHeight(boat.Boat, state.Meshes, boat.Ground);
            foreach (var member in boat.Berths) content.BoatTops[member.Id] = top;

            var start = content.Objects.Count;
            AddBoat(content.Objects, boat, state);
            content.AddUnit(start, null, boat, boat.Berths);

            // A boat ashore throws its shadow on the yard it stands in, not on the water below it. Only what is drawn
            // solid casts one.
            for (var i = start; i < content.Objects.Count; i++)
            {
                if (!content.Objects[i].IsTransparent) casters.Add(new ShadowCaster(content.Objects[i], boat.Ground ?? 0f));
            }
        }

        foreach (var berth in state.Berths)
        {
            if (!berth.IsVisible || !state.Filter.Includes(berth.Status)) continue;
            var start = content.Objects.Count;
            AddBerthStatus(content.Objects, berth, state, palette);
            content.AddUnit(start, berth, null, [berth]);
        }
    }

    /// <summary>Draws one unit of the berths layer again, as <see cref="BuildBerths"/> would now.</summary>
    public static void RebuildUnit(List<RenderObject> output, Berth? berth, BoatInstance? boat, SceneState state)
    {
        output.Clear();
        if (boat is { } instance) AddBoat(output, instance, state);
        else if (berth is not null) AddBerthStatus(output, berth, state, new Palette(state.Style));
    }

    /// <summary>True when the berth is drawn selected, and when (not being selected) it is drawn hovered.</summary>
    private static (bool Selected, bool Hovered) HighlightOf(Berth berth, SceneState state)
    {
        var isSelected = state.Selected.Contains(berth.Id);
        return (isSelected, !isSelected && berth.IsInteractive && string.Equals(berth.Id, state.HoveredBerthId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A berth's status pad, its buoy or post, and its label.</summary>
    private static void AddBerthStatus(List<RenderObject> output, Berth berth, SceneState state, Palette palette)
    {
        var colors = state.Colors;
        var selection = state.Style.Selection;
        var berthGround = GroundHeight(berth, state.LandLookup);
        var (isSelected, isHovered) = HighlightOf(berth, state);
        var statusColor = berth.IsDisabled ? colors.DisabledColor : colors.Get(berth.Status);
        var phase = BerthPlacement.AnimationPhase(berth.Id);
        var desaturation = berth.IsDisabled ? 1f : 0f;

        // Status pad on the water. A berth that can be hovered or selected keeps its pad even when the style hides pads,
        // drawn fully transparent, so that highlighting it rewrites the pad in place rather than adding one.
        var padAlpha = isSelected ? MathF.Max(colors.PadOpacity, 0.72f) : isHovered ? MathF.Max(colors.PadOpacity, 0.6f) : berth.IsDisabled ? colors.PadOpacity * 0.75f : colors.PadOpacity;
        var padEmissive = isSelected ? selection.SelectedGlow : isHovered ? selection.HoverGlow : 0.05f;
        if (padAlpha > 0.005f || berth.IsInteractive)
        {
            output.Add(new RenderObject(
                MeshIds.BerthPad, BerthPlacement.PadWorld(berth, berthGround), statusColor.WithAlpha(padAlpha > 0.005f ? padAlpha : 0f).ToVector4(), padEmissive,
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

        if (state.LabelMode.Includes(berth.Status)) AddLabel(output, berth, isSelected || isHovered, phase, berthGround, palette);
    }

    // ---- Highlight ----------------------------------------------------------------------------------

    /// <summary>The selection markers, standing above the tallest thing in each selected berth.</summary>
    /// <param name="output">Receives the markers.</param>
    /// <param name="state">The scene.</param>
    /// <param name="boatTops">Tops of the boats, from the berths layer.</param>
    public static void BuildHighlight(List<RenderObject> output, SceneState state, IReadOnlyDictionary<string, float> boatTops)
    {
        output.Clear();
        var selection = state.Style.Selection;
        if (!selection.ShowMarker || state.Selected.Count == 0) return;

        foreach (var berth in state.Berths)
        {
            if (!berth.IsVisible || !state.Filter.Includes(berth.Status) || !state.Selected.Contains(berth.Id)) continue;

            var isPrimary = string.Equals(berth.Id, state.PrimarySelectedId, StringComparison.OrdinalIgnoreCase);
            var baseGround = GroundHeight(berth, state.LandLookup) ?? 0f;
            var boatTop = boatTops.TryGetValue(berth.Id, out var t) ? t : baseGround + 3f;
            var markerPosition = MarinaMath.ToWorld(berth.Center, MarkerBaseHeight(boatTop, baseGround));
            output.Add(new RenderObject(
                MeshIds.SelectionMarker,
                Matrix4x4.CreateScale((isPrimary ? 2.4f : 1.7f) * selection.MarkerScale) * Matrix4x4.CreateTranslation(markerPosition),
                selection.MarkerTint.WithAlpha(1f).ToVector4(), 0.45f, RenderAnimation.SpinAndBob, isPrimary ? 0f : BerthPlacement.AnimationPhase(berth.Id)));
        }
    }

    /// <summary>
    /// The berth's name written flat on the water past its open end, one object per character. The characters sit just
    /// above the highest wave the water can reach (see <see cref="RenderAnimation.AboveWaves"/>), so waves never hide them.
    /// Land berths get their name on the ground, just above their pad.
    /// </summary>
    private static void AddLabel(List<RenderObject> output, Berth berth, bool highlighted, float phase, float? ground, Palette palette)
    {
        var text = berth.DisplayName.Trim();
        if (text.Length == 0) return;

        var font = palette.LabelFontFamily;
        var typeface = palette.LabelTypeface;
        var captured = palette.LabelFont;

        // How far the pen moves for each character. The built-in lettering is the same width throughout; a captured
        // font is not, so the line is laid out character by character either way.
        Span<float> advances = text.Length <= 64 ? stackalloc float[text.Length] : new float[text.Length];
        var total = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            advances[i] = captured is not null && captured.TryGetGlyph(text[i], out _)
                ? captured.AdvanceOf(text[i])
                : GlyphFont.AdvanceOf(font);
            total += advances[i];
        }

        var (center, height, upHeading, reading) = BerthPlacement.LabelPlacementForWidth(berth, total);
        // A name ashore is read against quay concrete or grass, not against the sea, so it gets its own
        // colour. Highlight and disabled still win: those say something about the berth, wherever it is.
        var tint = berth.IsDisabled ? palette.LabelDisabled
            : highlighted ? palette.LabelHighlight
            : berth.IsOnLand ? palette.LabelAshore
            : palette.Label;
        var scale = new Vector3(height, 1f, height);
        var pen = total * -0.5f;

        for (var i = 0; i < text.Length; i++)
        {
            var advance = advances[i];
            var drawn = captured is not null && captured.TryGetMeshId(text[i], out var meshId)
                || GlyphFont.TryGetMeshId(text[i], font, typeface, out meshId);

            if (drawn)
            {
                var position = center + reading * ((pen + (advance * 0.5f)) * height);
                output.Add(new RenderObject(
                    meshId,
                    MarinaMath.CreatePlacement(scale, upHeading, MarinaMath.ToWorld(position, ground is { } g ? g + LabelHeightAboveLand : LabelHeightAboveWater)),
                    tint, highlighted ? 0.35f : 0.15f, ground.HasValue ? RenderAnimation.None : RenderAnimation.AboveWaves, phase));
            }

            pen += advance;
        }
    }

    private static void AddBoat(List<RenderObject> output, BoatInstance boat, SceneState state)
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
            output.Add(new RenderObject(meshId, boat.World, tint, emissive, animation, phase, disabled ? 1f : 0f));
            return;
        }

        // Reserved / temporarily free: a "ghost" of the boat, tinted toward the status color.
        var statusColor = disabled ? colors.DisabledColor : colors.Get(boat.Status);
        var ghost = new ColorRgba(1f, 1f, 1f).Lerp(statusColor, colors.GhostBoatTint).WithAlpha(opacity);
        output.Add(new RenderObject(
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

    private static void AddPier(List<RenderObject> output, Pier pier, Palette p)
    {
        switch (pier.Type)
        {
            case PierType.Concrete: AddConcretePier(output, pier, p); break;
            case PierType.FloatingConcrete: AddFloatingConcretePier(output, pier, p); break;
            default: AddFloatingWoodenPier(output, pier, p); break;
        }
    }

    /// <summary>Fixed pier: thick slab on square columns, curbs along the edges, bollards.</summary>
    private static void AddConcretePier(List<RenderObject> output, Pier pier, Palette p)
    {
        const float slab = 0.45f;
        var top = pier.DeckHeight;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, slab, pier.Length), top - slab * 0.5f, p.ConcreteDeck));

        // Only the sides boats come alongside get a kerb: a pier against the quay has nothing to edge its back.
        var singleSided = pier.BerthingSides is PierSides.Left or PierSides.Right;
        foreach (var side in BerthSides(pier))
        {
            var height = singleSided ? 0.34f : 0.16f;
            var curbOffset = pier.Right * side * (pier.Width * 0.5f - 0.14f);
            output.Add(Box(pier, pier.Center + curbOffset, 0f, new Vector3(0.28f, height, pier.Length), top + height * 0.5f, p.ConcreteCurb));
        }

        var count = Math.Max(2, (int)MathF.Floor(pier.Length / pier.PilingSpacing) + 1);
        for (var i = 0; i < count; i++)
        {
            var along = 0.4f + (pier.Length - 0.8f) * i / (count - 1);
            foreach (var side in Sides)
            {
                var column = pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.35f);
                var height = top - slab + PilingDepth;
                output.Add(Box(pier, column, 0f, new Vector3(0.5f, height, 0.5f), top - slab - height * 0.5f, p.ConcreteColumn));

                if (i % 2 == 1 && pier.HasBerthsOn(side < 0f ? PierSide.Left : PierSide.Right))
                {
                    var bollard = pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.55f);
                    output.Add(Cylinder(bollard, top, new Vector3(0.32f, 0.38f, 0.32f), p.Bollard));
                }
            }
        }
    }

    /// <summary>Plank deck with walers on dark pontoon floats.</summary>
    private static void AddFloatingWoodenPier(List<RenderObject> output, Pier pier, Palette p)
    {
        const float deck = 0.14f;
        var top = pier.DeckHeight;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, deck, pier.Length), top - deck * 0.5f, p.WoodDeck));

        // Plank seams.
        const float plankPitch = 1.2f;
        for (var along = plankPitch; along < pier.Length - 0.1f; along += plankPitch)
        {
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width, 0.012f, 0.05f), top + 0.004f, p.WoodSeam));
        }

        foreach (var side in BerthSides(pier))
        {
            var waler = pier.Right * side * (pier.Width * 0.5f + 0.09f);
            output.Add(Box(pier, pier.Center + waler, 0f, new Vector3(0.18f, 0.3f, pier.Length), top - 0.17f, p.WoodWaler));
        }

        AddPontoonFloats(output, pier, top - deck, p);
    }

    /// <summary>Monolithic concrete pontoon with rubber fenders, section joints and cleats.</summary>
    private static void AddFloatingConcretePier(List<RenderObject> output, Pier pier, Palette p)
    {
        const float bottom = -0.35f;
        var top = pier.DeckHeight;
        var height = top - bottom;
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width, height - 0.04f, pier.Length), bottom + (height - 0.04f) * 0.5f, p.PontoonBody));
        output.Add(Box(pier, pier.Center, 0f, new Vector3(pier.Width - 0.16f, 0.04f, pier.Length - 0.16f), top - 0.02f, p.PontoonTop));

        foreach (var side in BerthSides(pier))
        {
            var fender = pier.Right * side * (pier.Width * 0.5f + 0.06f);
            output.Add(Box(pier, pier.Center + fender, 0f, new Vector3(0.12f, 0.22f, pier.Length), top - 0.16f, p.Rubber));
        }

        const float sectionLength = 12f;
        for (var along = sectionLength; along < pier.Length - 0.5f; along += sectionLength)
        {
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width - 0.1f, 0.012f, 0.04f), top + 0.004f, p.JointLine));
        }

        var cleats = Math.Max(2, (int)MathF.Floor(pier.Length / pier.PilingSpacing) + 1);
        for (var i = 0; i < cleats; i++)
        {
            var along = 0.6f + (pier.Length - 1.2f) * i / (cleats - 1);
            foreach (var side in BerthSides(pier))
            {
                output.Add(Cylinder(pier.Start + pier.Direction * along + pier.Right * side * (pier.Width * 0.5f - 0.3f), top, new Vector3(0.22f, 0.22f, 0.22f), p.Bollard));
            }
        }

    }

    /// <summary>
    /// Power and water pedestals (<see cref="Pier.Services"/>): one for every two berths on a berthing side, standing between them,
    /// so each berth has exactly one within reach. A lone berth gets one at the middle of its frontage, and a stretch of pier without
    /// berths gets none.
    /// </summary>
    private static void AddServicePedestals(List<RenderObject> output, Pier pier, IEnumerable<Berth> berths, Palette p)
    {
        const float postHeight = 0.95f;
        var top = pier.DeckHeight;

        foreach (var side in BerthSides(pier))
        {
            var offset = pier.Right * side * MathF.Max(0.12f, pier.Width * 0.5f - 0.3f);
            var row = BerthSpansAlong(pier, berths, side);
            for (var i = 0; i < row.Count; i += 2)
            {
                // One pedestal serves the pair, so it offers whatever the two of them together ask for.
                var services = ServicesOf(row[i].Berth, pier);
                if (i + 1 < row.Count) services |= ServicesOf(row[i + 1].Berth, pier);
                if (services == PierServices.None) continue;

                // Between the two berths of a pair, or halfway along a berth left on its own at the end of the row.
                var along = i + 1 < row.Count ? (row[i].Max + row[i + 1].Min) * 0.5f : (row[i].Min + row[i].Max) * 0.5f;
                var at = pier.Start + pier.Direction * Math.Clamp(along, 0.25f, MathF.Max(0.25f, pier.Length - 0.25f)) + offset;
                var power = (services & PierServices.Power) != 0;
                output.Add(Box(pier, at, 0f, new Vector3(0.28f, postHeight, 0.28f), top + postHeight * 0.5f, p.Pedestal));
                output.Add(Box(pier, at, 0f, new Vector3(0.34f, 0.1f, 0.34f), top + postHeight + 0.05f, power ? p.PowerTop : p.WaterTop));
                if (services == PierServices.PowerAndWater)
                {
                    output.Add(Box(pier, at, 0f, new Vector3(0.3f, 0.14f, 0.3f), top + postHeight * 0.45f, p.WaterTop));
                }
            }
        }
    }

    /// <summary>What a berth actually offers: its own setting, or the pier's when it has none of its own.</summary>
    internal static PierServices ServicesOf(Berth berth, Pier pier) => berth.Services ?? pier.Services;

    /// <summary>
    /// The stretch each berth on one side of a pier covers along it (distance from the pier's start to its near and far edges), in
    /// order along the pier.
    /// </summary>
    internal static IReadOnlyList<(Berth Berth, float Min, float Max)> BerthSpansAlong(Pier pier, IEnumerable<Berth> berths, float side)
    {
        ArgumentNullException.ThrowIfNull(pier);
        ArgumentNullException.ThrowIfNull(berths);
        var row = new List<(Berth Berth, float Min, float Max)>();
        foreach (var berth in berths)
        {
            if (!berth.IsVisible || !string.Equals(berth.PierId, pier.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (Vector2.Dot(berth.Center - pier.Center, pier.Right) * side <= 0f) continue;

            var along = Vector2.Dot(berth.Center - pier.Start, pier.Direction);
            var extent = MathF.Abs(Vector2.Dot(berth.Right, pier.Direction)) * berth.Width * 0.5f +
                         MathF.Abs(Vector2.Dot(berth.Forward, pier.Direction)) * berth.Length * 0.5f;
            row.Add((berth, along - extent, along + extent));
        }

        row.Sort((a, b) => a.Min.CompareTo(b.Min));
        return row;
    }

    private static void AddPontoonFloats(List<RenderObject> output, Pier pier, float underside, Palette p)
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
            output.Add(Box(pier, pier.Start + pier.Direction * along, 0f, new Vector3(pier.Width * 0.85f, height, segment), bottom + height * 0.5f, p.PontoonFloat));
        }
    }

    // ---- Finger piers and dividers ----------------------------------------------------------------

    private static void AddFingerPiers(List<RenderObject> output, Berth berth, Pier? pier, SceneState state, Palette p)
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
                pierType == PierType.FloatingWooden ? p.WoodFinger : p.PontoonTop));

            var outerEnd = edge + berth.Forward * (berth.Length * 0.5f - fingerLength);
            output.Add(pierType == PierType.FloatingWooden ? Piling(outerEnd, y + 1.2f, 0.32f) : SteelPile(outerEnd, y + 1.4f, 0.36f, p));
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

    private static void AddDivider(List<RenderObject> output, Divider divider, Pier? pier, Palette p)
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
                        output.Add(steel ? SteelPile(position, deckHeight + 1.4f, divider.Width, p) : Piling(position, deckHeight + 1.4f, divider.Width));
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
                            isEnd ? p.BoomEnd : p.BoomFloat, 0.1f, RenderAnimation.FloatOnWater, MarinaMath.StableHash01(divider.Id) * MathF.Tau + i * 0.7f));
                    }

                    break;
                }

            case DividerType.SinglePile:
                {
                    // Mediterranean mooring: one pile at the outer end of the boundary, nothing in between.
                    var steel = pier is { Type: not PierType.FloatingWooden };
                    output.Add(steel ? SteelPile(divider.End, deckHeight + 1.6f, divider.Width, p) : Piling(divider.End, deckHeight + 1.4f, divider.Width));
                    break;
                }

            default:
                {
                    var wooden = pier is null or { Type: PierType.FloatingWooden };
                    var y = MathF.Min(deckHeight, 0.6f) - 0.12f - FingerThickness * 0.5f;
                    output.Add(new RenderObject(
                        MeshIds.UnitBox,
                        MarinaMath.CreatePlacement(new Vector3(divider.Width, FingerThickness, divider.Length), divider.HeadingDegrees, MarinaMath.ToWorld(divider.Center, y)),
                        wooden ? p.WoodFinger : p.PontoonTop));
                    output.Add(wooden ? Piling(divider.End, y + 1.2f, 0.34f) : SteelPile(divider.End, y + 1.4f, 0.38f, p));
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
            LabelAshore = Opaque(style.Labels.AshoreColor.ToVector3());
            LabelHighlight = Opaque(style.Labels.HighlightColor.ToVector3());
            LabelDisabled = Opaque(style.Labels.DisabledColor.ToVector3());
            LabelFontFamily = style.Labels.FontFamily;
            LabelTypeface = style.Labels.Typeface;
            LabelFont = style.Labels.Font;
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
        public Vector4 LabelAshore { get; }
        public Vector4 LabelHighlight { get; }
        public Vector4 LabelDisabled { get; }

        /// <summary>The face berth labels are set in.</summary>
        public LabelFont LabelFontFamily { get; }

        public LabelTypeface LabelTypeface { get; }

        public LabelFontDefinition? LabelFont { get; }

        private static Vector4 Opaque(Vector3 color) => new(Vector3.Clamp(color, Vector3.Zero, Vector3.One), 1f);
    }

    private static RenderObject SteelPile(Vector2 position, float topHeight, float diameter, Palette p) =>
        new(MeshIds.Cylinder,
            Matrix4x4.CreateScale(diameter, topHeight + PilingDepth, diameter) *
            Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, -PilingDepth)),
            p.Steel);
}
