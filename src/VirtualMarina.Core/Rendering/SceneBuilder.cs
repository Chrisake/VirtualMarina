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
    public required IEnumerable<Dock> Docks { get; init; }

    public required IEnumerable<Slip> Slips { get; init; }

    public required IEnumerable<Divider> Dividers { get; init; }

    public required IEnumerable<LandArea> Land { get; init; }

    public required Func<string, Slip?> SlipLookup { get; init; }

    public required Func<string, Dock?> DockLookup { get; init; }

    public required Func<string, MultiSlipBerth?> BerthLookup { get; init; }

    public required SlipStatusFilter Filter { get; init; }

    /// <summary>Selected slip ids (case-insensitive set).</summary>
    public required IReadOnlySet<string> Selected { get; init; }

    public required string? PrimarySelectedId { get; init; }

    public required string? HoveredSlipId { get; init; }

    public SlipLabelMode LabelMode { get; init; }

    public required StatusColorScheme Colors { get; init; }

    public required MeshLibrary Meshes { get; init; }
}

/// <summary>Turns domain state into a flat, backend-neutral list of render objects.</summary>
internal static class SceneBuilder
{
    private static readonly Vector4 White = Vector4.One;
    private static readonly float[] Sides = { -1f, 1f };

    // Wood
    private static readonly Vector4 WoodDeck = new(0.66f, 0.50f, 0.33f, 1f);
    private static readonly Vector4 WoodSeam = new(0.43f, 0.31f, 0.20f, 1f);
    private static readonly Vector4 WoodWaler = new(0.50f, 0.37f, 0.24f, 1f);
    private static readonly Vector4 WoodFinger = new(0.70f, 0.55f, 0.38f, 1f);
    private static readonly Vector4 PontoonFloat = new(0.20f, 0.21f, 0.23f, 1f);

    // Concrete
    private static readonly Vector4 ConcreteDeck = new(0.74f, 0.73f, 0.70f, 1f);
    private static readonly Vector4 ConcreteCurb = new(0.84f, 0.83f, 0.80f, 1f);
    private static readonly Vector4 ConcreteColumn = new(0.58f, 0.57f, 0.55f, 1f);
    private static readonly Vector4 PontoonBody = new(0.68f, 0.67f, 0.64f, 1f);
    private static readonly Vector4 PontoonTop = new(0.83f, 0.82f, 0.79f, 1f);
    private static readonly Vector4 JointLine = new(0.45f, 0.45f, 0.44f, 1f);
    private static readonly Vector4 Rubber = new(0.12f, 0.12f, 0.13f, 1f);
    private static readonly Vector4 Steel = new(0.56f, 0.58f, 0.61f, 1f);
    private static readonly Vector4 Bollard = new(0.17f, 0.18f, 0.20f, 1f);

    // Booms
    private static readonly Vector4 BoomFloat = new(0.96f, 0.56f, 0.12f, 1f);
    private static readonly Vector4 BoomEnd = new(0.98f, 0.84f, 0.15f, 1f);
    private static readonly Vector4 BoomLine = new(0.25f, 0.25f, 0.27f, 1f);

    // Slip labels
    private static readonly Vector4 LabelColor = new(0.97f, 0.98f, 1f, 1f);
    private static readonly Vector4 LabelHighlight = new(1f, 0.90f, 0.35f, 1f);
    private static readonly Vector4 LabelDisabled = new(0.62f, 0.64f, 0.66f, 1f);
    /// <summary>Clearance above the highest possible wave; the shader adds the wave bound (AboveWaves).</summary>
    public const float LabelHeightAboveWater = 0.04f;

    private const float FingerWidth = 0.7f;
    private const float FingerThickness = 0.25f;
    private const float PilingDepth = 2.5f;

    /// <summary>Height of the selection marker above the tallest thing in the slip (see <see cref="MarkerBaseHeight"/>).</summary>
    public const float MarkerClearance = 2.2f;

    /// <summary>Top of the selection marker (including bob) relative to its base.</summary>
    public const float MarkerTop = 4.3f;

    public static float MarkerBaseHeight(float boatTop) => MathF.Max(boatTop, 2.5f) + MarkerClearance;

    public static void Build(List<RenderObject> output, SceneState state)
    {
        output.Clear();
        var transparent = new List<RenderObject>();
        var colors = state.Colors;

        foreach (var area in state.Land) AddLand(output, area);
        foreach (var dock in state.Docks) AddDock(output, dock);
        foreach (var divider in state.Dividers) AddDivider(output, divider, divider.DockId is null ? null : state.DockLookup(divider.DockId));

        var slips = state.Slips as IReadOnlyCollection<Slip> ?? state.Slips.ToList();

        // Boats first, so the selection marker can sit above them.
        var boatTops = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (var boat in SlipPlacement.EnumerateBoats(slips, state.SlipLookup, state.BerthLookup, state.Filter))
        {
            var top = SlipPlacement.BoatTopHeight(boat.Boat, state.Meshes);
            foreach (var member in boat.Slips) boatTops[member.Id] = top;
            AddBoat(output, transparent, boat, state);
        }

        foreach (var slip in slips)
        {
            // Hidden slips draw nothing at all, not even their physical structure.
            if (!slip.IsVisible) continue;

            var dock = state.DockLookup(slip.DockId);
            if (slip.HasFingerPiers) AddFingerPiers(output, slip, dock, state);

            // The status filter hides status visuals and boats, but not structure.
            if (!state.Filter.Includes(slip.Status)) continue;

            var isSelected = state.Selected.Contains(slip.Id);
            var isHovered = !isSelected && slip.IsInteractive && string.Equals(slip.Id, state.HoveredSlipId, StringComparison.OrdinalIgnoreCase);
            var statusColor = slip.IsDisabled ? colors.DisabledColor : colors.Get(slip.Status);
            var phase = SlipPlacement.AnimationPhase(slip.Id);
            var desaturation = slip.IsDisabled ? 1f : 0f;

            // Status pad on the water.
            var padAlpha = isSelected ? 0.72f : isHovered ? 0.6f : slip.IsDisabled ? colors.PadOpacity * 0.75f : colors.PadOpacity;
            var padEmissive = isSelected ? 0.45f : isHovered ? 0.25f : 0.05f;
            transparent.Add(new RenderObject(
                MeshIds.SlipPad, SlipPlacement.PadWorld(slip), statusColor.WithAlpha(padAlpha).ToVector4(), padEmissive,
                isSelected ? RenderAnimation.Pulse : RenderAnimation.None, phase, desaturation));

            // Status buoy, visible from far away.
            output.Add(new RenderObject(
                MeshIds.Buoy,
                Matrix4x4.CreateScale(0.9f) * Matrix4x4.CreateTranslation(SlipPlacement.BuoyPosition(slip)),
                statusColor.WithAlpha(1f).ToVector4(), slip.IsDisabled ? 0.05f : 0.35f, RenderAnimation.FloatOnWater, phase, desaturation));

            if (state.LabelMode.Includes(slip.Status)) AddLabel(output, slip, isSelected || isHovered, phase);

            if (isSelected)
            {
                var isPrimary = string.Equals(slip.Id, state.PrimarySelectedId, StringComparison.OrdinalIgnoreCase);
                var boatTop = boatTops.TryGetValue(slip.Id, out var t) ? t : 3f;
                var markerPosition = MarinaMath.ToWorld(slip.Center, MarkerBaseHeight(boatTop));
                output.Add(new RenderObject(
                    MeshIds.SelectionMarker,
                    Matrix4x4.CreateScale(isPrimary ? 2.4f : 1.7f) * Matrix4x4.CreateTranslation(markerPosition),
                    White, 0.45f, RenderAnimation.SpinAndBob, isPrimary ? 0f : phase));
            }
        }

        output.AddRange(transparent);
    }

    /// <summary>
    /// The slip's name written flat on the water past its open end, one object per character. The characters sit just
    /// above the highest wave the water can reach (see <see cref="RenderAnimation.AboveWaves"/>), so waves never hide them.
    /// </summary>
    private static void AddLabel(List<RenderObject> output, Slip slip, bool highlighted, float phase)
    {
        var text = slip.DisplayName.Trim();
        if (text.Length == 0) return;

        var (center, height, upHeading, reading) = SlipPlacement.LabelPlacement(slip, text.Length);
        var start = center - reading * (GlyphFont.MeasureWidth(text.Length) * height * 0.5f - GlyphFont.GlyphWidth * height * 0.5f);
        var tint = slip.IsDisabled ? LabelDisabled : highlighted ? LabelHighlight : LabelColor;
        var scale = new Vector3(height, 1f, height);

        for (var i = 0; i < text.Length; i++)
        {
            if (!GlyphFont.TryGetMeshId(text[i], out var meshId)) continue;
            var position = start + reading * (i * GlyphFont.Advance * height);
            output.Add(new RenderObject(
                meshId,
                MarinaMath.CreatePlacement(scale, upHeading, MarinaMath.ToWorld(position, LabelHeightAboveWater)),
                tint, highlighted ? 0.35f : 0.15f, RenderAnimation.AboveWaves, phase));
        }
    }

    private static void AddBoat(List<RenderObject> output, List<RenderObject> transparent, BoatInstance boat, SceneState state)
    {
        var colors = state.Colors;
        var isSelected = boat.Slips.Any(s => state.Selected.Contains(s.Id));
        var isHovered = !isSelected && state.HoveredSlipId is { } hovered &&
            boat.Slips.Any(s => s.IsInteractive && string.Equals(s.Id, hovered, StringComparison.OrdinalIgnoreCase));
        var disabled = boat.IsDisabled;
        var animation = RenderAnimation.FloatOnWater | (isSelected ? RenderAnimation.Pulse : RenderAnimation.None);
        var emissive = isSelected ? 0.3f : isHovered ? 0.15f : 0f;
        var phase = SlipPlacement.AnimationPhase(boat.BerthId ?? boat.PrimarySlip.Id);
        var meshId = MeshIds.ForBoat(boat.Boat.Type);

        if (!boat.Status.ShowsGhostBoat())
        {
            var tint = disabled ? new Vector4(0.92f, 0.92f, 0.92f, 1f) : White;
            output.Add(new RenderObject(meshId, boat.World, tint, emissive, animation, phase, disabled ? 1f : 0f));
            return;
        }

        // Reserved / temporarily free: translucent "ghost" of the boat, tinted toward the status color.
        var statusColor = disabled ? colors.DisabledColor : colors.Get(boat.Status);
        var ghost = new ColorRgba(1f, 1f, 1f).Lerp(statusColor, 0.55f).WithAlpha(colors.GhostBoatOpacity);
        transparent.Add(new RenderObject(meshId, boat.World, ghost.ToVector4(), disabled ? emissive : MathF.Max(emissive, 0.25f), animation, phase, disabled ? 1f : 0f));
    }

    private static void AddLand(List<RenderObject> output, LandArea area)
    {
        const float depth = 3f;
        var color = area.Kind switch
        {
            LandKind.Breakwater => new Vector4(0.52f, 0.51f, 0.49f, 1f),
            LandKind.Grass => new Vector4(0.40f, 0.58f, 0.30f, 1f),
            _ => new Vector4(0.74f, 0.72f, 0.67f, 1f),
        };

        var world = MarinaMath.CreatePlacement(
            new Vector3(area.Area.Width, area.Height + depth, area.Area.Length),
            area.Area.HeadingDegrees,
            MarinaMath.ToWorld(area.Area.Center, (area.Height - depth) * 0.5f));
        output.Add(new RenderObject(MeshIds.UnitBox, world, color));
    }

    // ---- Docks --------------------------------------------------------------------------------------

    private static void AddDock(List<RenderObject> output, Dock dock)
    {
        switch (dock.Type)
        {
            case DockType.Concrete: AddConcreteDock(output, dock); break;
            case DockType.FloatingConcrete: AddFloatingConcreteDock(output, dock); break;
            default: AddFloatingWoodenDock(output, dock); break;
        }
    }

    /// <summary>Fixed pier: thick slab on square columns, curbs along the edges, bollards.</summary>
    private static void AddConcreteDock(List<RenderObject> output, Dock dock)
    {
        const float slab = 0.45f;
        var top = dock.DeckHeight;
        output.Add(Box(dock, dock.Center, 0f, new Vector3(dock.Width, slab, dock.Length), top - slab * 0.5f, ConcreteDeck));

        foreach (var side in Sides)
        {
            var curbOffset = dock.Right * side * (dock.Width * 0.5f - 0.14f);
            output.Add(Box(dock, dock.Center + curbOffset, 0f, new Vector3(0.28f, 0.16f, dock.Length), top + 0.08f, ConcreteCurb));
        }

        var count = Math.Max(2, (int)MathF.Floor(dock.Length / dock.PilingSpacing) + 1);
        for (var i = 0; i < count; i++)
        {
            var along = 0.4f + (dock.Length - 0.8f) * i / (count - 1);
            foreach (var side in Sides)
            {
                var column = dock.Start + dock.Direction * along + dock.Right * side * (dock.Width * 0.5f - 0.35f);
                var height = top - slab + PilingDepth;
                output.Add(Box(dock, column, 0f, new Vector3(0.5f, height, 0.5f), top - slab - height * 0.5f, ConcreteColumn));

                if (i % 2 == 1)
                {
                    var bollard = dock.Start + dock.Direction * along + dock.Right * side * (dock.Width * 0.5f - 0.55f);
                    output.Add(Cylinder(bollard, top, new Vector3(0.32f, 0.38f, 0.32f), Bollard));
                }
            }
        }
    }

    /// <summary>Plank deck with walers on dark pontoon floats, held by wooden guide piles.</summary>
    private static void AddFloatingWoodenDock(List<RenderObject> output, Dock dock)
    {
        const float deck = 0.14f;
        var top = dock.DeckHeight;
        output.Add(Box(dock, dock.Center, 0f, new Vector3(dock.Width, deck, dock.Length), top - deck * 0.5f, WoodDeck));

        // Plank seams.
        const float plankPitch = 1.2f;
        for (var along = plankPitch; along < dock.Length - 0.1f; along += plankPitch)
        {
            output.Add(Box(dock, dock.Start + dock.Direction * along, 0f, new Vector3(dock.Width, 0.012f, 0.05f), top + 0.004f, WoodSeam));
        }

        foreach (var side in Sides)
        {
            var waler = dock.Right * side * (dock.Width * 0.5f + 0.09f);
            output.Add(Box(dock, dock.Center + waler, 0f, new Vector3(0.18f, 0.3f, dock.Length), top - 0.17f, WoodWaler));
        }

        AddPontoonFloats(output, dock, top - deck);
        AddGuidePiles(output, dock, steel: false);
    }

    /// <summary>Monolithic concrete pontoon with rubber fenders, section joints and steel guide piles.</summary>
    private static void AddFloatingConcreteDock(List<RenderObject> output, Dock dock)
    {
        const float bottom = -0.35f;
        var top = dock.DeckHeight;
        var height = top - bottom;
        output.Add(Box(dock, dock.Center, 0f, new Vector3(dock.Width, height - 0.04f, dock.Length), bottom + (height - 0.04f) * 0.5f, PontoonBody));
        output.Add(Box(dock, dock.Center, 0f, new Vector3(dock.Width - 0.16f, 0.04f, dock.Length - 0.16f), top - 0.02f, PontoonTop));

        foreach (var side in Sides)
        {
            var fender = dock.Right * side * (dock.Width * 0.5f + 0.06f);
            output.Add(Box(dock, dock.Center + fender, 0f, new Vector3(0.12f, 0.22f, dock.Length), top - 0.16f, Rubber));
        }

        const float sectionLength = 12f;
        for (var along = sectionLength; along < dock.Length - 0.5f; along += sectionLength)
        {
            output.Add(Box(dock, dock.Start + dock.Direction * along, 0f, new Vector3(dock.Width - 0.1f, 0.012f, 0.04f), top + 0.004f, JointLine));
        }

        var cleats = Math.Max(2, (int)MathF.Floor(dock.Length / dock.PilingSpacing) + 1);
        for (var i = 0; i < cleats; i++)
        {
            var along = 0.6f + (dock.Length - 1.2f) * i / (cleats - 1);
            foreach (var side in Sides)
            {
                output.Add(Cylinder(dock.Start + dock.Direction * along + dock.Right * side * (dock.Width * 0.5f - 0.3f), top, new Vector3(0.22f, 0.22f, 0.22f), Bollard));
            }
        }

        AddGuidePiles(output, dock, steel: true);
    }

    private static void AddPontoonFloats(List<RenderObject> output, Dock dock, float underside)
    {
        const float bottom = -0.35f;
        const float segment = 4f;
        const float pitch = 4.6f;
        var height = underside - bottom;
        if (height <= 0.02f) return;

        var count = Math.Max(1, (int)MathF.Floor((dock.Length - 0.4f) / pitch));
        var used = count * pitch - (pitch - segment);
        var start = (dock.Length - used) * 0.5f;
        for (var i = 0; i < count; i++)
        {
            var along = start + i * pitch + segment * 0.5f;
            output.Add(Box(dock, dock.Start + dock.Direction * along, 0f, new Vector3(dock.Width * 0.85f, height, segment), bottom + height * 0.5f, PontoonFloat));
        }
    }

    private static void AddGuidePiles(List<RenderObject> output, Dock dock, bool steel)
    {
        var spacing = MathF.Max(dock.PilingSpacing * 2f, 8f);
        var count = Math.Max(2, (int)MathF.Floor(dock.Length / spacing) + 1);
        for (var i = 0; i < count; i++)
        {
            var along = 1f + (dock.Length - 2f) * i / (count - 1);
            // Alternate sides so the dock reads as floating rather than on stilts.
            var side = i % 2 == 0 ? -1f : 1f;
            var position = dock.Start + dock.Direction * along + dock.Right * side * (dock.Width * 0.5f + (steel ? 0.5f : 0.35f));
            output.Add(steel ? SteelPile(position, dock.DeckHeight + 1.8f, 0.55f) : Piling(position, dock.DeckHeight + 1.6f, 0.4f));
        }
    }

    // ---- Finger piers and dividers ----------------------------------------------------------------

    private static void AddFingerPiers(List<RenderObject> output, Slip slip, Dock? dock, SceneState state)
    {
        var deckHeight = dock?.DeckHeight ?? 0.6f;
        var dockType = dock?.Type ?? DockType.FloatingWooden;
        var berth = slip.BerthId is { } berthId ? state.BerthLookup(berthId) : null;
        var fingerLength = slip.Length * 0.75f;
        var y = MathF.Min(deckHeight, 0.6f) - 0.12f - FingerThickness * 0.5f;

        foreach (var side in Sides)
        {
            // A boat spanning several slips lies across the fingers between them, so those aren't drawn.
            if (berth is not null && SharesEdgeWithBerthMember(slip, side, berth, state)) continue;

            var edge = slip.Center + slip.Right * side * (slip.Width * 0.5f);
            var center = edge + slip.Forward * (slip.Length * 0.5f - fingerLength * 0.5f);
            output.Add(new RenderObject(
                MeshIds.UnitBox,
                MarinaMath.CreatePlacement(new Vector3(FingerWidth, FingerThickness, fingerLength), slip.HeadingDegrees, MarinaMath.ToWorld(center, y)),
                dockType == DockType.FloatingWooden ? WoodFinger : PontoonTop));

            var outerEnd = edge + slip.Forward * (slip.Length * 0.5f - fingerLength);
            output.Add(dockType == DockType.FloatingWooden ? Piling(outerEnd, y + 1.2f, 0.32f) : SteelPile(outerEnd, y + 1.4f, 0.36f));
        }
    }

    private static bool SharesEdgeWithBerthMember(Slip slip, float side, MultiSlipBerth berth, SceneState state)
    {
        var probe = slip.Center + slip.Right * side * (slip.Width * 0.5f + 0.35f);
        foreach (var id in berth.SlipIds)
        {
            if (string.Equals(id, slip.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (state.SlipLookup(id) is { IsVisible: true } other && other.Bounds.Contains(probe)) return true;
        }

        return false;
    }

    private static void AddDivider(List<RenderObject> output, Divider divider, Dock? dock)
    {
        var deckHeight = dock?.DeckHeight ?? 0.5f;
        switch (divider.Type)
        {
            case DividerType.Piles:
            {
                var count = Math.Max(2, (int)MathF.Floor(divider.Length / divider.Spacing) + 1);
                var steel = dock is { Type: not DockType.FloatingWooden };
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

            default:
            {
                var wooden = dock is null or { Type: DockType.FloatingWooden };
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

    /// <summary>Box aligned with the dock's heading, centered at <paramref name="plan"/> and <paramref name="centerY"/>.</summary>
    private static RenderObject Box(Dock dock, Vector2 plan, float headingOffset, Vector3 size, float centerY, Vector4 color) =>
        new(MeshIds.UnitBox, MarinaMath.CreatePlacement(size, dock.HeadingDegrees + headingOffset, MarinaMath.ToWorld(plan, centerY)), color);

    private static RenderObject Cylinder(Vector2 plan, float baseY, Vector3 size, Vector4 color) =>
        new(MeshIds.Cylinder, Matrix4x4.CreateScale(size) * Matrix4x4.CreateTranslation(MarinaMath.ToWorld(plan, baseY)), color);

    private static RenderObject Piling(Vector2 position, float topHeight, float diameter = 0.32f) =>
        new(MeshIds.Piling,
            Matrix4x4.CreateScale(diameter, topHeight + PilingDepth, diameter) *
            Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, -PilingDepth)),
            Vector4.One);

    private static RenderObject SteelPile(Vector2 position, float topHeight, float diameter) =>
        new(MeshIds.Cylinder,
            Matrix4x4.CreateScale(diameter, topHeight + PilingDepth, diameter) *
            Matrix4x4.CreateTranslation(MarinaMath.ToWorld(position, -PilingDepth)),
            Steel);
}
