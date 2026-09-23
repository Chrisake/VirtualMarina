using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Mathematics;

namespace VirtualMarina.Core.Design;

/// <summary>The settings the tools draw with. Ranges and defaults come from <see cref="DesignerDefaults"/>.</summary>
public sealed partial class MarinaDesigner
{
    private LandKind _landKind = DesignerDefaults.LandKind;
    private float _landHeight = DesignerDefaults.LandHeight.Default;
    private PierType _pierType = DesignerDefaults.PierType;
    private float _pierWidth = DesignerDefaults.PierWidth.Default;
    private PierSides _pierSides = DesignerDefaults.PierBerthingSides;
    private float _berthWidth = DesignerDefaults.BerthWidth.Default;
    private float _berthLength = DesignerDefaults.BerthLength.Default;
    private float _berthDepth = DesignerDefaults.BerthDepth.Default;
    private BerthSeparator _berthSeparators = DesignerDefaults.BerthSeparators;
    private float _berthGap = DesignerDefaults.BerthGap.Default;
    private bool _alignBerths = true;
    private PierServices _berthServices = DesignerDefaults.BerthServices;
    private BerthNamingScheme _berthNaming = BerthNamingScheme.Default;
    private string _pierNamePattern = DesignerDefaults.PierNamePattern;
    private float _landBerthHeading = DesignerDefaults.LandBerthHeading.Default;
    private float _snapPixels = DesignerDefaults.SnapDistancePixels.Default;
    private float _fogFactor = DesignerDefaults.FogFactor.Default;
    private float _treeDensity = DesignerDefaults.TreeDensity.Default;
    private HinterlandScenery _scenery = DesignerDefaults.Scenery;

    /// <summary>Kind of land area drawn by <see cref="DesignTool.DrawLandArea"/>. Default <see cref="Domain.LandKind.Quay"/>.</summary>
    public LandKind LandKind
    {
        get => _landKind;
        set => SetSetting(ref _landKind, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Top height of new land areas above the water, 0–50 m (default 1).</summary>
    public float LandHeight
    {
        get => _landHeight;
        set => SetSetting(ref _landHeight, DesignerDefaults.LandHeight.Require(value));
    }

    /// <summary>
    /// What is scattered across the mainland drawn by <see cref="DesignTool.DrawShoreline"/>. Default
    /// <see cref="HinterlandScenery.Countryside"/>.
    /// </summary>
    public HinterlandScenery Scenery
    {
        get => _scenery;
        set => SetSetting(ref _scenery, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Trees per 1000 m² scattered on new lawns (<see cref="Domain.LandKind.Grass"/>) and by <see cref="DesignTool.PlantTrees"/>, 0–100
    /// (default 8; 0 = no trees). Positions are random when drawn and then stored with the land area, so they stay put.
    /// </summary>
    public float TreeDensity
    {
        get => _treeDensity;
        set => SetSetting(ref _treeDensity, DesignerDefaults.TreeDensity.Require(value));
    }

    /// <summary>Construction of piers drawn by <see cref="DesignTool.DrawPier"/>. Default <see cref="Domain.PierType.FloatingWooden"/>.</summary>
    public PierType PierType
    {
        get => _pierType;
        set => SetSetting(ref _pierType, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Deck width of new piers, 0.5–30 m (default 2.5).</summary>
    public float PierWidth
    {
        get => _pierWidth;
        set => SetSetting(ref _pierWidth, DesignerDefaults.PierWidth.Require(value));
    }

    /// <summary>Berthing sides of new piers (<see cref="Pier.BerthingSides"/>). Default <see cref="PierSides.Both"/>.</summary>
    public PierSides PierBerthingSides
    {
        get => _pierSides;
        set => SetSetting(ref _pierSides, DesignerDefaults.IsValidBerthingSides(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>Width of new berths (along the pier), 1–50 m (default 5).</summary>
    public float BerthWidth
    {
        get => _berthWidth;
        set => SetSetting(ref _berthWidth, DesignerDefaults.BerthWidth.Require(value));
    }

    /// <summary>Length of new berths (away from the pier), 1–150 m (default 12).</summary>
    public float BerthLength
    {
        get => _berthLength;
        set => SetSetting(ref _berthLength, DesignerDefaults.BerthLength.Require(value));
    }

    /// <summary>Water depth of new berths, stored as <see cref="Berth.MaxDraft"/>, 0.1–50 m (default 3).</summary>
    public float BerthDepth
    {
        get => _berthDepth;
        set => SetSetting(ref _berthDepth, DesignerDefaults.BerthDepth.Require(value));
    }

    /// <summary>
    /// What separates new berths: their own finger piers (default), nothing at all, or generated <see cref="Divider"/> elements
    /// (finger pier, piles, boom or a single pile at the outer end).
    /// </summary>
    public BerthSeparator BerthSeparators
    {
        get => _berthSeparators;
        set => SetSetting(ref _berthSeparators, Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Space left between neighbouring berths, 0–20 m (default 0, berths touching). With <see cref="BerthSeparator.None"/> at least
    /// <see cref="MinimumSeparatorGap"/> is used, so the berths never touch without a separator.
    /// </summary>
    public float BerthGap
    {
        get => _berthGap;
        set => SetSetting(ref _berthGap, DesignerDefaults.BerthGap.Require(value));
    }

    /// <summary>
    /// True (default): a row of berths lines up with the nearest existing berth edge on that side, or with the pier's start, in whole
    /// berth pitches. False: the first berth starts exactly where you click (or at <c>fromAlong</c>), at any offset from the pier's start.
    /// </summary>
    public bool AlignBerthsToExisting
    {
        get => _alignBerths;
        set => SetSetting(ref _alignBerths, value);
    }

    /// <summary>
    /// Power/water pedestals switched on for a pier when berths are added to it (default <see cref="PierServices.None"/>). They are
    /// drawn on the berthing sides only, next to the berths that exist (see <see cref="Pier.Services"/>).
    /// </summary>
    public PierServices BerthServices
    {
        get => _berthServices;
        set => SetSetting(ref _berthServices, DesignerDefaults.IsValidServices(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// How the berths drawn from now on are named: the pattern, the first number and the step between them
    /// (default <see cref="BerthNamingScheme.Default"/>, giving <c>A-L01</c>, <c>A-L02</c>, ...).
    /// </summary>
    /// <exception cref="ArgumentException">The scheme could not name anything (see <see cref="BerthNamingScheme.Validate"/>).</exception>
    /// <example><code>designer.BerthNaming = new BerthNamingScheme { Pattern = "{number}", StartNumber = 101, Increment = 2 };</code></example>
    public BerthNamingScheme BerthNaming
    {
        get => _berthNaming;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Validate().FirstOrDefault() is { } problem) throw new ArgumentException(problem, nameof(value));
            SetSetting(ref _berthNaming, value);
        }
    }

    /// <summary>
    /// Name given to a pier as it is drawn (default <c>Pier {pier}</c>, e.g. "Pier A"). <c>{pier}</c> stands for the
    /// pier's generated id; text without it names every new pier the same, which is allowed — names need not be unique.
    /// </summary>
    /// <exception cref="ArgumentException">The pattern is empty.</exception>
    /// <seealso cref="RenamePier"/>
    public string PierNamePattern
    {
        get => _pierNamePattern;
        set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            SetSetting(ref _pierNamePattern, value);
        }
    }

    /// <summary>
    /// Direction a boat stored on a land berth points, in degrees (0° = +Z, 90° = +X; default 0). Used by
    /// <see cref="DesignTool.AddLandBerths"/> when both clicks land on the same spot, and by <see cref="CreateLandBerth"/> by default.
    /// </summary>
    public float LandBerthHeading
    {
        get => _landBerthHeading;
        set => SetSetting(ref _landBerthHeading, float.IsFinite(value) ? MarinaMath.DeltaAngle(0f, value) : throw new ArgumentOutOfRangeException(nameof(value), value, null));
    }

    /// <summary>
    /// Points within this many pixels of an existing corner, pier end, coast point, berth corner or land edge snap to it
    /// (default 12, 0 turns snapping off). Holding Alt also turns it off.
    /// </summary>
    public float SnapDistancePixels
    {
        get => _snapPixels;
        set => SetSetting(ref _snapPixels, DesignerDefaults.SnapDistancePixels.Require(value));
    }

    /// <summary>
    /// Multiplier for the distance fog while the designer is active (default 0.15; 1 keeps the normal fog), so the marina and the
    /// reference image stay clear when seen from high above.
    /// </summary>
    public float FogFactor
    {
        get => _fogFactor;
        set
        {
            var checkedValue = DesignerDefaults.FogFactor.Require(value);
            if (checkedValue == _fogFactor) return;
            _fogFactor = checkedValue;

            // The fog is part of the lighting of the whole scene, not of the preview.
            InvalidateScene();
            RaiseStateChanged();
        }
    }

    /// <summary>Sets a setting that shows in the tools' previews, and says so when it changed.</summary>
    private void SetSetting<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        InvalidateOverlay();
        RaiseStateChanged();
    }
}
