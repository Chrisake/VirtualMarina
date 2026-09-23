using VirtualMarina.Core.Api;

namespace VirtualMarina.Core.Rendering;

/// <summary>
/// Everything that controls how the marina looks and moves: lighting, water and waves, status colors and boat opacities, land and
/// trees, piers, labels, selection highlights and the camera. Available as <see cref="MarinaVisualizer.Style"/> (and
/// <c>MarinaViewControl.Style</c> / <c>&lt;MarinaView MarinaStyle="..."&gt;</c>).
/// </summary>
/// <remarks>
/// Change properties at any time; the view updates on the next frame. Assign a whole new instance to switch themes. Sections that
/// affect geometry (<see cref="Land"/>) rebuild the affected meshes when they change.
/// </remarks>
/// <example>
/// <code>
/// var style = marina.Style;
/// style.Water.WaveAmplitude = 0.02f;         // calm water
/// style.Water.SkyReflection = 0.3f;          // fewer cloud-like reflections on the water
/// style.Status.ReservedBoatOpacity = 0.7f;
/// style.Status.FreeColor = ColorRgba.FromHex("#00C853");
/// style.Land.ShowTrees = false;
///
/// marina.Style = new MarinaStyle { View = new ViewStyle { FieldOfViewDegrees = 35 } };   // or replace everything
/// </code>
/// </example>
public sealed class MarinaStyle
{
    /// <summary>Sun, ambient light, specular highlights, sky and fog. Applied every frame.</summary>
    public LightingSettings Lighting { get; init; } = new();

    /// <summary>Water colors, waves (height, length, speed), reflections, ripples, sun glints and how much boats move. Applied every frame.</summary>
    public WaterSettings Water { get; init; } = new();

    /// <summary>Status colors, pad and boat opacities per status, disabled color and status markers (buoys and posts).</summary>
    public StatusColorScheme Status { get; init; } = new();

    /// <summary>Colors of quays, lawns, breakwater rocks and trees, and whether trees are shown.</summary>
    public LandStyle Land { get; init; } = new();

    /// <summary>Colors of wooden and concrete piers, floats, fenders, bollards, piles, booms and service pedestals.</summary>
    public StructureStyle Piers { get; init; } = new();

    /// <summary>Colors of berth names written on the water.</summary>
    public LabelStyle Labels { get; init; } = new();

    /// <summary>Selection marker and hover/selection highlights.</summary>
    public SelectionStyle Selection { get; init; } = new();

    /// <summary>Camera field of view and animation smoothing.</summary>
    public ViewStyle View { get; init; } = new();

    /// <summary>Whether the boats and piers cast shadows on the ground, and how dark those shadows are.</summary>
    public ShadowStyle Shadows { get; init; } = new();

    /// <summary>A copy of the defaults.</summary>
    public static MarinaStyle CreateDefault() => new();

    /// <summary>
    /// A deep copy: every section is a new instance with the same values, so changing the copy leaves this style (and
    /// any visualizer showing it) alone. The captured label font is shared, as it is never changed once built.
    /// </summary>
    /// <remarks>
    /// Each section copies itself (an internal <c>CopyFrom</c> kept next to its fields), so a setting added to a section is added
    /// to the copy in the same place. <see cref="WaterSettings.GridResolution"/> is fixed when the water section is built, so it
    /// goes into the new section's initializer.
    /// </remarks>
    public MarinaStyle Clone()
    {
        var copy = new MarinaStyle { Water = new WaterSettings { GridResolution = Water.GridResolution } };
        copy.Lighting.CopyFrom(Lighting);
        copy.Water.CopyFrom(Water);
        copy.Status.CopyFrom(Status);
        copy.Land.CopyFrom(Land);
        copy.Piers.CopyFrom(Piers);
        copy.Labels.CopyFrom(Labels);
        copy.Selection.CopyFrom(Selection);
        copy.View.CopyFrom(View);
        copy.Shadows.CopyFrom(Shadows);
        return copy;
    }

    internal IEnumerable<StyleSection> SceneSections => new StyleSection[] { Status, Piers, Labels, Selection, Shadows };
}

/// <summary>Base of the style sections that notify the visualizer when they change.</summary>
public abstract class StyleSection
{
    /// <summary>A property of this section changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Stores a value and raises <see cref="Changed"/> when it differs.</summary>
    protected void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnChanged();
    }

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>Clamps to a range, treating NaN as <paramref name="min"/>.</summary>
    protected static float Clamp(float value, float min, float max) => float.IsNaN(value) ? min : Math.Clamp(value, min, max);
}

/// <summary>Colors of land areas and their trees (<see cref="MarinaStyle.Land"/>). Changing them rebuilds the land meshes.</summary>
public sealed class LandStyle : StyleSection
{
    private ColorRgba _quay = new(0.74f, 0.72f, 0.67f);
    private ColorRgba _quayWall = new(0.62f, 0.60f, 0.56f);
    private ColorRgba _grass = new(0.40f, 0.58f, 0.30f);
    private ColorRgba _grassBank = new(0.47f, 0.40f, 0.30f);
    private ColorRgba _rock = new(0.53f, 0.51f, 0.48f);
    private ColorRgba _foliage = new(0.24f, 0.46f, 0.20f);
    private ColorRgba _conifer = new(0.16f, 0.36f, 0.22f);
    private ColorRgba _trunk = new(0.38f, 0.27f, 0.17f);
    private ColorRgba _palm = new(0.33f, 0.52f, 0.26f);
    private ColorRgba _blossom = new(0.95f, 0.72f, 0.80f);
    private ColorRgba _building = new(0.80f, 0.78f, 0.73f);
    private ColorRgba _roof = new(0.59f, 0.33f, 0.25f);
    private bool _showTrees = true;
    private float _rockVariation = 0.2f;

    /// <summary>Top of quays (light concrete).</summary>
    public ColorRgba QuayColor { get => _quay; set => SetField(ref _quay, value); }

    /// <summary>Sides of quays.</summary>
    public ColorRgba QuayWallColor { get => _quayWall; set => SetField(ref _quayWall, value); }

    /// <summary>Top of lawns.</summary>
    public ColorRgba GrassColor { get => _grass; set => SetField(ref _grass, value); }

    /// <summary>Sides of lawns (earth).</summary>
    public ColorRgba GrassBankColor { get => _grassBank; set => SetField(ref _grassBank, value); }

    /// <summary>Average color of breakwater rocks.</summary>
    public ColorRgba RockColor { get => _rock; set => SetField(ref _rock, value); }

    /// <summary>How much individual rocks vary in brightness, 0–0.5 (default 0.2).</summary>
    public float RockColorVariation { get => _rockVariation; set => SetField(ref _rockVariation, Clamp(value, 0f, 0.5f)); }

    /// <summary>Crowns of broadleaf trees.</summary>
    public ColorRgba FoliageColor { get => _foliage; set => SetField(ref _foliage, value); }

    /// <summary>Crowns of conifers.</summary>
    public ColorRgba ConiferColor { get => _conifer; set => SetField(ref _conifer, value); }

    /// <summary>Tree trunks.</summary>
    public ColorRgba TrunkColor { get => _trunk; set => SetField(ref _trunk, value); }

    /// <summary>Fronds of palms.</summary>
    public ColorRgba PalmColor { get => _palm; set => SetField(ref _palm, value); }

    /// <summary>Blossom of cherry trees (<see cref="Domain.TreeShape.Cherry"/>).</summary>
    public ColorRgba BlossomColor { get => _blossom; set => SetField(ref _blossom, value); }

    /// <summary>Walls of the buildings in a <see cref="Domain.HinterlandScenery.Town"/> behind the shore.</summary>
    public ColorRgba BuildingColor { get => _building; set => SetField(ref _building, value); }

    /// <summary>Roofs of those buildings.</summary>
    public ColorRgba RoofColor { get => _roof; set => SetField(ref _roof, value); }

    /// <summary>Draw the trees of land areas (<c>LandArea.Trees</c>). Default true.</summary>
    public bool ShowTrees { get => _showTrees; set => SetField(ref _showTrees, value); }

    internal void CopyFrom(LandStyle other)
    {
        QuayColor = other.QuayColor;
        QuayWallColor = other.QuayWallColor;
        GrassColor = other.GrassColor;
        GrassBankColor = other.GrassBankColor;
        RockColor = other.RockColor;
        RockColorVariation = other.RockColorVariation;
        FoliageColor = other.FoliageColor;
        ConiferColor = other.ConiferColor;
        TrunkColor = other.TrunkColor;
        PalmColor = other.PalmColor;
        BlossomColor = other.BlossomColor;
        BuildingColor = other.BuildingColor;
        RoofColor = other.RoofColor;
        ShowTrees = other.ShowTrees;
    }
}

/// <summary>Colors of piers and their fittings (<see cref="MarinaStyle.Piers"/>). Seams, walers, curbs and columns are shades of these.</summary>
public sealed class StructureStyle : StyleSection
{
    private ColorRgba _wood = new(0.66f, 0.50f, 0.33f);
    private ColorRgba _concrete = new(0.74f, 0.73f, 0.70f);
    private ColorRgba _float = new(0.20f, 0.21f, 0.23f);
    private ColorRgba _fender = new(0.12f, 0.12f, 0.13f);
    private ColorRgba _bollard = new(0.17f, 0.18f, 0.20f);
    private ColorRgba _steel = new(0.56f, 0.58f, 0.61f);
    private ColorRgba _boomFloat = new(0.96f, 0.56f, 0.12f);
    private ColorRgba _boomEnd = new(0.98f, 0.84f, 0.15f);
    private ColorRgba _pedestal = new(0.82f, 0.83f, 0.85f);
    private ColorRgba _power = new(0.95f, 0.76f, 0.11f);
    private ColorRgba _water = new(0.16f, 0.52f, 0.85f);

    /// <summary>Deck of wooden piers and wooden finger piers.</summary>
    public ColorRgba WoodColor { get => _wood; set => SetField(ref _wood, value); }

    /// <summary>Deck of concrete piers, pontoons and their finger piers.</summary>
    public ColorRgba ConcreteColor { get => _concrete; set => SetField(ref _concrete, value); }

    /// <summary>Pontoon floats under wooden floating piers.</summary>
    public ColorRgba FloatColor { get => _float; set => SetField(ref _float, value); }

    /// <summary>Rubber fenders of floating concrete piers.</summary>
    public ColorRgba FenderColor { get => _fender; set => SetField(ref _fender, value); }

    /// <summary>Bollards and cleats.</summary>
    public ColorRgba BollardColor { get => _bollard; set => SetField(ref _bollard, value); }

    /// <summary>Steel piles (pile dividers and finger pier ends next to concrete piers).</summary>
    public ColorRgba SteelColor { get => _steel; set => SetField(ref _steel, value); }

    /// <summary>Floats of boom dividers.</summary>
    public ColorRgba BoomFloatColor { get => _boomFloat; set => SetField(ref _boomFloat, value); }

    /// <summary>End floats of boom dividers.</summary>
    public ColorRgba BoomEndColor { get => _boomEnd; set => SetField(ref _boomEnd, value); }

    /// <summary>Body of the power/water pedestals along a pier (<c>Pier.Services</c>).</summary>
    public ColorRgba PedestalColor { get => _pedestal; set => SetField(ref _pedestal, value); }

    /// <summary>Top of pedestals that supply electricity.</summary>
    public ColorRgba PowerColor { get => _power; set => SetField(ref _power, value); }

    /// <summary>Top (or band) of pedestals that supply water.</summary>
    public ColorRgba WaterColor { get => _water; set => SetField(ref _water, value); }

    internal void CopyFrom(StructureStyle other)
    {
        WoodColor = other.WoodColor;
        ConcreteColor = other.ConcreteColor;
        FloatColor = other.FloatColor;
        FenderColor = other.FenderColor;
        BollardColor = other.BollardColor;
        SteelColor = other.SteelColor;
        BoomFloatColor = other.BoomFloatColor;
        BoomEndColor = other.BoomEndColor;
        PedestalColor = other.PedestalColor;
        PowerColor = other.PowerColor;
        WaterColor = other.WaterColor;
    }
}

/// <summary>Colors of berth names written on the water (<see cref="MarinaStyle.Labels"/>; see <c>BerthLabelMode</c>).</summary>
public sealed class LabelStyle : StyleSection
{
    private ColorRgba _color = new(0.97f, 0.98f, 1f);
    private ColorRgba _ashore = new(0.16f, 0.18f, 0.20f);
    private ColorRgba _highlight = new(1f, 0.90f, 0.35f);
    private ColorRgba _disabled = new(0.62f, 0.64f, 0.66f);
    private Geometry.LabelFont _font = Geometry.LabelFont.Regular;
    private Geometry.LabelTypeface _typeface = Geometry.LabelTypeface.Sans;
    private Geometry.LabelFontDefinition? _fontDefinition;

    /// <summary>
    /// The face berth labels are set in. These are built-in stroke faces rather than system typefaces, so the
    /// choice is between a few weights and widths (see <see cref="Geometry.LabelFont"/>).
    /// </summary>
    public Geometry.LabelFont FontFamily { get => _font; set => SetField(ref _font, Enum.IsDefined(value) ? value : Geometry.LabelFont.Regular); }

    /// <summary>
    /// The shape of the letters, as against <see cref="FontFamily"/>, which is their weight and width. Default
    /// <see cref="Geometry.LabelTypeface.Sans"/>.
    /// </summary>
    /// <remarks>
    /// The labels are drawn as strokes rather than set in a real font, so that both backends render them the same
    /// and the library carries no font files. See <see cref="Geometry.LabelTypeface"/>.
    /// </remarks>
    public Geometry.LabelTypeface Typeface { get => _typeface; set => SetField(ref _typeface, Enum.IsDefined(value) ? value : Geometry.LabelTypeface.Sans); }

    /// <summary>
    /// A real font captured into the design, used in place of the built-in lettering. Null (the default) draws the
    /// labels with <see cref="Typeface"/> and <see cref="FontFamily"/> instead.
    /// </summary>
    /// <remarks>
    /// The outlines travel with the design, so the marina looks the same on a machine that does not have the font
    /// installed. A character the captured font does not carry falls back to the built-in lettering, so a name with
    /// something unusual in it still reads. See <see cref="Geometry.LabelFontDefinition"/>.
    /// </remarks>
    public Geometry.LabelFontDefinition? Font { get => _fontDefinition; set => SetField(ref _fontDefinition, value); }

    /// <summary>Normal label color, for berths on the water.</summary>
    public ColorRgba Color { get => _color; set => SetField(ref _color, value); }

    /// <summary>
    /// Label of a berth ashore (<see cref="Domain.Berth.IsOnLand"/>). Dark by default, because a name ashore is
    /// read against quay concrete or grass rather than against water, and <see cref="Color"/> is near-white so it
    /// carries on the dark sea. Set it to <see cref="Color"/> to letter both the same way again.
    /// </summary>
    public ColorRgba AshoreColor { get => _ashore; set => SetField(ref _ashore, value); }

    /// <summary>Label of a hovered or selected berth, on the water or ashore.</summary>
    public ColorRgba HighlightColor { get => _highlight; set => SetField(ref _highlight, value); }

    /// <summary>Label of a disabled berth.</summary>
    public ColorRgba DisabledColor { get => _disabled; set => SetField(ref _disabled, value); }

    /// <summary>Takes every value from <paramref name="other"/>; the captured font is shared, as it never changes once built.</summary>
    internal void CopyFrom(LabelStyle other)
    {
        FontFamily = other.FontFamily;
        Typeface = other.Typeface;
        Font = other.Font;
        Color = other.Color;
        AshoreColor = other.AshoreColor;
        HighlightColor = other.HighlightColor;
        DisabledColor = other.DisabledColor;
    }
}

/// <summary>Selection marker and highlight strengths (<see cref="MarinaStyle.Selection"/>).</summary>
public sealed class SelectionStyle : StyleSection
{
    private bool _showMarker = true;
    private ColorRgba _markerTint = new(1f, 1f, 1f);
    private float _markerScale = 1f;
    private float _selectedGlow = 0.45f;
    private float _hoverGlow = 0.25f;
    private bool _pulse = true;

    /// <summary>Show the spinning marker above selected berths. Default true.</summary>
    public bool ShowMarker { get => _showMarker; set => SetField(ref _showMarker, value); }

    /// <summary>Multiplied with the marker's gold color (white keeps it gold).</summary>
    public ColorRgba MarkerTint { get => _markerTint; set => SetField(ref _markerTint, value); }

    /// <summary>Marker size multiplier, 0.2–5 (default 1).</summary>
    public float MarkerScale { get => _markerScale; set => SetField(ref _markerScale, Clamp(value, 0.2f, 5f)); }

    /// <summary>Glow of selected pads and boats, 0–1 (default 0.45).</summary>
    public float SelectedGlow { get => _selectedGlow; set => SetField(ref _selectedGlow, Clamp(value, 0f, 1f)); }

    /// <summary>Glow of the hovered pad and boat, 0–1 (default 0.25).</summary>
    public float HoverGlow { get => _hoverGlow; set => SetField(ref _hoverGlow, Clamp(value, 0f, 1f)); }

    /// <summary>Pulse the glow of selected berths. Default true.</summary>
    public bool Pulse { get => _pulse; set => SetField(ref _pulse, value); }

    internal void CopyFrom(SelectionStyle other)
    {
        ShowMarker = other.ShowMarker;
        MarkerTint = other.MarkerTint;
        MarkerScale = other.MarkerScale;
        SelectedGlow = other.SelectedGlow;
        HoverGlow = other.HoverGlow;
        Pulse = other.Pulse;
    }
}

/// <summary>
/// Shadows cast on the ground by the boats and the piers (<see cref="MarinaStyle.Shadows"/>).
/// </summary>
/// <remarks>
/// Each shadow is the object itself squashed onto the ground along the sun's rays, so it costs one more instance per
/// object. That is cheap enough for a phone but not free on a large marina, which is what <see cref="IsEnabled"/> is
/// for. See <see cref="Geometry.ShadowProjection"/> for what this kind of shadow can and cannot do.
/// </remarks>
public sealed class ShadowStyle : StyleSection
{
    private bool _isEnabled = true;
    private float _strength = 0.25f;

    /// <summary>Cast shadows at all. Default true; turning it off drops every shadow instance from the scene.</summary>
    public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }

    /// <summary>
    /// How dark a shadow is, 0–1 (default 0.25). A flattened object overlaps itself, so the darkness on screen is
    /// rather more than this; past about 0.4 the overlaps start to show as blotches.
    /// </summary>
    public float Strength { get => _strength; set => SetField(ref _strength, Clamp(value, 0f, 1f)); }

    internal void CopyFrom(ShadowStyle other)
    {
        IsEnabled = other.IsEnabled;
        Strength = other.Strength;
    }
}

/// <summary>Camera optics and motion (<see cref="MarinaStyle.View"/>). Applied to <c>MarinaVisualizer.Camera</c> when they change.</summary>
public sealed class ViewStyle : StyleSection
{
    private float _fieldOfView = 45f;
    private float _smoothing = 10f;

    /// <summary>Vertical field of view in degrees, 15–100 (default 45).</summary>
    public float FieldOfViewDegrees { get => _fieldOfView; set => SetField(ref _fieldOfView, Clamp(value, 15f, 100f)); }

    /// <summary>How quickly the camera eases toward a new position; 0 jumps instantly (default 10).</summary>
    public float CameraSmoothing { get => _smoothing; set => SetField(ref _smoothing, Clamp(value, 0f, 100f)); }

    internal void CopyFrom(ViewStyle other)
    {
        FieldOfViewDegrees = other.FieldOfViewDegrees;
        CameraSmoothing = other.CameraSmoothing;
    }
}
