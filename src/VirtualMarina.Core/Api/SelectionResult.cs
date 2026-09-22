namespace VirtualMarina.Core.Api;

/// <summary>Why a berth id passed to <see cref="MarinaVisualizer.SetSelection(IEnumerable{string}, bool, VirtualMarina.Core.Camera.CameraAngle?)"/> was not selected.</summary>
public enum BerthSelectionRejection
{
    /// <summary>No berth has this id.</summary>
    NotFound,

    /// <summary>The berth is disabled (<c>IsDisabled</c>).</summary>
    Disabled,

    /// <summary>The berth is hidden (<c>IsVisible = false</c>).</summary>
    Hidden,

    /// <summary>The berth's status is excluded by the status filter.</summary>
    FilteredOut,
}

/// <summary>A berth id that <c>SetSelection</c> skipped.</summary>
/// <param name="BerthId">The id as passed in.</param>
/// <param name="Reason">Why it was skipped.</param>
public sealed record RejectedBerth(string BerthId, BerthSelectionRejection Reason);

/// <summary>Outcome of a selection change made through the API.</summary>
/// <param name="SelectedBerthIds">The selection after the call, in selection order (the last one is primary).</param>
/// <param name="Rejected">Ids that were skipped, with the reason.</param>
/// <param name="Changed">False when the selection was already exactly this.</param>
public sealed record SelectionResult(IReadOnlyList<string> SelectedBerthIds, IReadOnlyList<RejectedBerth> Rejected, bool Changed)
{
    /// <summary>Number of selected berths.</summary>
    public int Count => SelectedBerthIds.Count;

    /// <summary>True when nothing is selected.</summary>
    public bool IsEmpty => SelectedBerthIds.Count == 0;
}
