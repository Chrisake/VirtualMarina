using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Input;

namespace VirtualMarina.Core.Design;

/// <summary>What finishing the drawing came to, which a right-click and Enter each need to tell apart.</summary>
internal enum DraftOutcome
{
    /// <summary>There was nothing to finish, or making the element was refused; the drawing is still there or already gone.</summary>
    Nothing,

    /// <summary>The element was made.</summary>
    Created,

    /// <summary>The drawing moved on a step without making anything: a coast settled and waiting for its side.</summary>
    Advanced,

    /// <summary>The drawing cannot be finished as it stands (an outline crossing itself), and is kept to be put right.</summary>
    Invalid,
}

/// <summary>
/// What one <see cref="DesignTool"/> does with the pointer and the keys, and the drawing it keeps while it does it. The
/// designer hands every input to the handler of the tool in hand; everything a tool needs to remember lives in its
/// handler, and is forgotten with <see cref="Reset"/> when the tool is put down.
/// </summary>
internal abstract class DesignToolHandler
{
    protected DesignToolHandler(MarinaDesigner designer) => Designer = designer;

    /// <summary>The one-line instruction for the tool as things stand.</summary>
    public abstract string Hint { get; }

    /// <summary>True while a drawing is in progress, so Escape, Enter and Backspace have something to act on.</summary>
    public virtual bool HasDraft => false;

    /// <summary>The points placed so far, in plan coordinates. The same instance is returned until they change.</summary>
    public virtual IReadOnlyList<Vector2> DraftPoints => Array.Empty<Vector2>();

    /// <summary>Height of the plane the pointer is picked on: the one the drawing is shown on.</summary>
    public virtual float PointerPlane => 0f;

    /// <summary>True when the pointer snaps to corners and edges near it.</summary>
    public virtual bool Snaps => false;

    /// <summary>True when a left drag belongs to the tool rather than to the camera.</summary>
    public virtual bool CapturesDrag => false;

    /// <summary>True while such a drag is under way.</summary>
    public virtual bool IsDragging => false;

    /// <summary>True when the Delete key would do something now.</summary>
    public virtual bool WantsDelete => false;

    protected MarinaDesigner Designer { get; }

    protected MarinaVisualizer Marina => Designer.Marina;

    protected DesignPicker Picker => Designer.Picker;

    protected Vector2? Pointer => Designer.PointerPosition;

    /// <summary>Called before the pointer position is worked out, to find what is under it.</summary>
    public virtual void OnHover(float x, float y)
    {
    }

    /// <summary>Last say on the pointer position once it has snapped, e.g. squaring a pier up.</summary>
    public virtual Vector2 AdjustPointer(Vector2 point, bool snapped) => point;

    /// <summary>A left click.</summary>
    public virtual void OnClick(float x, float y, InputModifiers modifiers)
    {
    }

    /// <summary>
    /// A right click: finishes the drawing, or drops it when there is nothing to finish. An outline that crosses itself is
    /// kept, so the corner at fault can be taken back rather than every corner drawn again.
    /// </summary>
    public virtual void OnRightClick(float x, float y)
    {
        if (Complete() == DraftOutcome.Nothing) Designer.CancelDraft();
    }

    public virtual void OnDoubleClick()
    {
    }

    /// <summary>Finishes the drawing (Enter, a right-click, <see cref="MarinaDesigner.CompleteDraft"/>).</summary>
    public virtual DraftOutcome Complete() => DraftOutcome.Nothing;

    /// <summary>
    /// Ends the drawing, however it ended, and raises <see cref="MarinaDesigner.DraftChanged"/> when there was one.
    /// Returns false when there was nothing to end.
    /// </summary>
    public virtual bool EndDraft(DesignDraftChange change) => false;

    /// <summary>Takes back the last point placed. Returns false when there was none.</summary>
    public virtual bool RemoveLastPoint() => false;

    /// <summary>Whether a change in these modifiers would show in the tool's preview.</summary>
    public virtual bool ShowsModifiers(InputModifiers changed) => false;

    public virtual void BeginDrag(float x, float y)
    {
    }

    public virtual void Drag(float x, float y)
    {
    }

    public virtual void EndDrag()
    {
    }

    /// <summary>Abandons a drag under way, putting back what it moved. Returns false when there was none.</summary>
    public virtual bool CancelDrag() => false;

    /// <summary>The Delete key. Returns true when it did something.</summary>
    public virtual bool Delete() => false;

    /// <summary>The pointer left the view.</summary>
    public virtual void ClearHover()
    {
    }

    /// <summary>The tool is being put down (another tool picked, or the designer switched off): forget what is in hand.</summary>
    public virtual void Reset()
    {
        CancelDrag();
        ClearHover();
    }

    /// <summary>Draws the tool's preview.</summary>
    public virtual void AppendOverlay(DesignOverlay overlay)
    {
    }
}

/// <summary>A tool that places points one click at a time and keeps them as its drawing.</summary>
internal abstract class DraftTool : DesignToolHandler
{
    private readonly List<Vector2> _points = [];
    private Vector2[]? _snapshot;

    protected DraftTool(MarinaDesigner designer)
        : base(designer)
    {
    }

    public override bool HasDraft => _points.Count > 0;

    public override IReadOnlyList<Vector2> DraftPoints => _snapshot ??= [.. _points];

    protected IReadOnlyList<Vector2> Points => _points;

    public override bool EndDraft(DesignDraftChange change)
    {
        var points = DraftPoints;
        var hadDraft = HasDraft;
        ClearDraft();
        if (hadDraft) Designer.RaiseDraftChanged(change, change == DesignDraftChange.Canceled ? Array.Empty<Vector2>() : points);
        return hadDraft;
    }

    public override bool RemoveLastPoint()
    {
        if (_points.Count == 0) return false;
        _points.RemoveAt(_points.Count - 1);
        PointsChanged();
        Designer.RaiseDraftChanged(DesignDraftChange.PointRemoved, DraftPoints);
        return true;
    }

    protected void AddPoint(Vector2 point)
    {
        _points.Add(point);
        PointsChanged();
        Designer.RaiseDraftChanged(DesignDraftChange.PointAdded, DraftPoints);
    }

    /// <summary>Puts points back in one go (a settled coast taken back to be drawn on), without announcing it.</summary>
    protected void RestorePoints(IEnumerable<Vector2> points)
    {
        _points.AddRange(points);
        PointsChanged();
    }

    /// <summary>Forgets the drawing without announcing it.</summary>
    protected virtual void ClearDraft()
    {
        _points.Clear();
        PointsChanged();
    }

    /// <summary>Called whenever the points change, so what is worked out from them can be forgotten.</summary>
    protected virtual void OnPointsChanged()
    {
    }

    /// <summary>The pointer when it is somewhere other than the last point placed: where the next point would go.</summary>
    protected List<Vector2> PointsWithPointer() =>
        Pointer is { } p && (_points.Count == 0 || Vector2.DistanceSquared(_points[^1], p) > 1e-6f) ? [.. _points, p] : [.. _points];

    private void PointsChanged()
    {
        _snapshot = null;
        OnPointsChanged();
    }
}
