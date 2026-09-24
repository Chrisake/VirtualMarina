using System.Numerics;
using VirtualMarina.Core.Api;
using VirtualMarina.Core.Domain;
using VirtualMarina.Core.Input;
using VirtualMarina.Core.Mathematics;
using VirtualMarina.Core.Resources;

namespace VirtualMarina.Core.Design;

/// <summary>Pointer and keyboard input, forwarded by <see cref="MarinaInputController"/> to the tool in hand.</summary>
public sealed partial class MarinaDesigner
{
    /// <summary>
    /// The smallest north-up box around what is being dragged with <see cref="DesignTool.SelectArea"/>, or null.
    /// </summary>
    /// <remarks>
    /// The box the user drags follows the camera rather than the compass, so this is only its extent.
    /// <see cref="SelectionQuad"/> has the corners to draw.
    /// </remarks>
    public (Vector2 Min, Vector2 Max)? SelectionBox =>
        SelectionQuad is { } quad
            ? (quad.Aggregate(Vector2.Min), quad.Aggregate(Vector2.Max))
            : null;

    /// <summary>
    /// The four corners of the box being dragged with <see cref="DesignTool.SelectArea"/>, in plan coordinates, or
    /// null when nothing is being dragged.
    /// </summary>
    /// <remarks>
    /// Its sides run with the camera, so dragging across the screen selects what the box appeared to cover, whichever
    /// way the view happens to be turned. The heading is taken when the drag starts, so turning the camera part way
    /// through does not reshape what has already been swept.
    /// </remarks>
    public IReadOnlyList<Vector2>? SelectionQuad => ((SelectAreaTool)_handlers[(int)DesignTool.SelectArea]).Quad;

    /// <summary>
    /// Selects every berth whose middle lies inside a north-up box in plan coordinates. Kept for code that wants a
    /// box in compass terms; the tool itself uses the overload that takes a heading.
    /// </summary>
    /// <param name="from">One corner of the box.</param>
    /// <param name="to">The opposite corner.</param>
    /// <param name="add">True to add to the selection already made, false to replace it.</param>
    /// <returns>The berths now selected.</returns>
    public IReadOnlyList<string> SelectBerthsInArea(Vector2 from, Vector2 to, bool add = false) =>
        SelectBerthsInArea(from, to, 0f, add);

    /// <summary>
    /// Selects every berth whose middle lies inside a box whose sides run along <paramref name="headingDegrees"/>.
    /// This is what <see cref="DesignTool.SelectArea"/> does when the drag ends, using the camera heading.
    /// </summary>
    /// <param name="from">One corner of the box.</param>
    /// <param name="to">The opposite corner.</param>
    /// <param name="headingDegrees">Which way the top of the box points; 0 is north.</param>
    /// <param name="add">True to add to the selection already made, false to replace it.</param>
    /// <returns>The berths now selected.</returns>
    public IReadOnlyList<string> SelectBerthsInArea(Vector2 from, Vector2 to, float headingDegrees, bool add = false)
    {
        var (right, forward) = ViewAxes(headingDegrees);
        var (minU, maxU) = Extent(Vector2.Dot(from, right), Vector2.Dot(to, right));
        var (minV, maxV) = Extent(Vector2.Dot(from, forward), Vector2.Dot(to, forward));

        var inside = Marina.GetBerths()
            .Where(berth => berth.IsInteractive)
            .Where(berth =>
            {
                var u = Vector2.Dot(berth.Center, right);
                var v = Vector2.Dot(berth.Center, forward);
                return u >= minU && u <= maxU && v >= minV && v <= maxV;
            })
            .Select(berth => berth.Id);

        var ids = add ? Marina.SelectedBerths.Select(berth => berth.Id).Concat(inside).Distinct(StringComparer.OrdinalIgnoreCase) : inside;
        return Marina.SetSelection(ids.ToArray()).SelectedBerthIds;
    }

    /// <summary>The corners of a box between two plan points whose sides run along a heading.</summary>
    internal static Vector2[] BoxCorners(Vector2 from, Vector2 to, float headingDegrees)
    {
        var (right, forward) = ViewAxes(headingDegrees);
        var (minU, maxU) = Extent(Vector2.Dot(from, right), Vector2.Dot(to, right));
        var (minV, maxV) = Extent(Vector2.Dot(from, forward), Vector2.Dot(to, forward));
        return
        [
            right * minU + forward * minV,
            right * maxU + forward * minV,
            right * maxU + forward * maxV,
            right * minU + forward * maxV,
        ];
    }

    /// <summary>True when a left drag belongs to a tool — moving the picture or dragging a selection box — not the camera.</summary>
    internal bool CapturesDrag(PointerButton button) => _active && button == PointerButton.Left && _handler.CapturesDrag;

    internal void BeginImageDrag(float x, float y) => _handler.BeginDrag(x, y);

    internal void DragImage(float x, float y) => _handler.Drag(x, y);

    internal void EndImageDrag() => _handler.EndDrag();

    /// <summary>
    /// Tells the designer which modifier keys are held, without the pointer having moved. Returns true when it
    /// redrew because of it.
    /// </summary>
    /// <remarks>
    /// Alt changes what the tools are about to do — the eraser takes the whole row rather than one berth, a point
    /// stops snapping — and the preview has to say so the moment the key goes down, not the next time the pointer
    /// happens to move. Without this the marina sits there showing the wrong thing while the user holds the key
    /// still and wonders whether it worked.
    /// </remarks>
    /// <param name="modifiers">The modifier keys now held.</param>
    internal bool SetModifiers(InputModifiers modifiers)
    {
        if (_modifiers == modifiers) return false;

        var before = _modifiers;
        _modifiers = modifiers;
        if (!_active || !_handler.ShowsModifiers(before ^ modifiers)) return false;

        InvalidateOverlay();
        return true;
    }

    internal void HandlePointerMove(float x, float y, InputModifiers modifiers)
    {
        _modifiers = modifiers;

        // What is under the pointer first: some tools pick the pointer on the plane of the land it is over.
        _handler.OnHover(x, y);
        UpdatePointer(x, y);
        InvalidateOverlay();
    }

    internal void HandlePointerLeave()
    {
        _pointer = null;
        _handler.ClearHover();
        InvalidateOverlay();
    }

    internal void HandleClick(float x, float y, PointerButton button, InputModifiers modifiers)
    {
        // The click is where the pointer came to rest, so what is under it is looked up once, here, and the tool acts on that.
        HandlePointerMove(x, y, modifiers);

        if (button == PointerButton.Right) _handler.OnRightClick(x, y);
        else if (button == PointerButton.Left) _handler.OnClick(x, y, modifiers);
    }

    internal void HandleDoubleClick(float x, float y, PointerButton button, InputModifiers modifiers)
    {
        if (button == PointerButton.Left) _handler.OnDoubleClick();
    }

    /// <summary>True when <see cref="HandleKey"/> would act on the key right now.</summary>
    internal bool WantsKey(MarinaKey key) => _active && key switch
    {
        MarinaKey.Escape => _handler.IsDragging || HasDraft || (EscapeReturnsToNavigate && _tool != DesignTool.Navigate),
        MarinaKey.Enter => DraftPoints.Count > 0,
        MarinaKey.Backspace => HasDraft,
        MarinaKey.Delete => _handler.WantsDelete,
        MarinaKey.Undo => HasDraft || CanUndo,
        MarinaKey.Redo => CanRedo,
        _ => false,
    };

    /// <summary>
    /// A key for the designer. Escape abandons a drag, then the drawing, then (with <see cref="EscapeReturnsToNavigate"/>) puts the tool
    /// down. Ctrl+Z takes back the last point while drawing, and undoes the last change otherwise. Returns true when the key did
    /// something.
    /// </summary>
    internal bool HandleKey(MarinaKey key)
    {
        switch (key)
        {
            case MarinaKey.Enter:
                // A coast settled by Enter made nothing yet, but the key did its job all the same.
                return _handler.Complete() is DraftOutcome.Created or DraftOutcome.Advanced;
            case MarinaKey.Backspace:
                return RemoveLastPoint();
            case MarinaKey.Delete:
                return _handler.Delete();
            case MarinaKey.Undo:
                return TryUndo();
            case MarinaKey.Redo:
                return TryRedo();
            case MarinaKey.Escape:
                if (_handler.CancelDrag() || CancelDraft()) return true;
                if (!EscapeReturnsToNavigate || _tool == DesignTool.Navigate) return false;
                Tool = DesignTool.Navigate;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The plan directions the sides of a box run along, for a box turned to the given heading.</summary>
    private static (Vector2 Right, Vector2 Forward) ViewAxes(float headingDegrees) =>
        (MarinaMath.HeadingToRight(headingDegrees), MarinaMath.HeadingToDirection(headingDegrees));

    private static (float Min, float Max) Extent(float a, float b) => a <= b ? (a, b) : (b, a);

    /// <summary>
    /// Undo or redo straight from the host's input loop, which has no business catching a change that could not be made. The change
    /// stays where it was, and <see cref="ActionFailed"/> says why.
    /// </summary>
    private bool TryHistory(bool undo)
    {
        var description = undo ? UndoDescription : RedoDescription;
        try
        {
            return undo ? Undo() : Redo();
        }
        catch (Exception ex) when (ex is MarinaLayoutException or InvalidOperationException)
        {
            ReportFailure(Strings.Format(undo ? Strings.UndoFailed : Strings.RedoFailed, description), ex);
            return false;
        }
    }

    /// <summary>
    /// Works out where the pointer is: on the plane the tool draws on, snapped to what is near when the tool snaps, then given
    /// the tool's last say (a pier squared up).
    /// </summary>
    private void UpdatePointer(float x, float y)
    {
        PointerSnapped = false;
        HeadingSnapped = false;
        var height = _handler.PointerPlane;
        if (Picker.PlanPointAt(x, y, height) is not { } point)
        {
            _pointer = null;
            return;
        }

        var snapped = false;
        if (_handler.Snaps && _snapPixels > 0f && (_modifiers & InputModifiers.Alt) == 0)
        {
            point = Picker.Snap(x, y, point, _handler.DraftPoints, height, _snapPixels, out snapped);
        }

        PointerSnapped = snapped;
        _pointer = _handler.AdjustPointer(point, snapped);
    }
}
