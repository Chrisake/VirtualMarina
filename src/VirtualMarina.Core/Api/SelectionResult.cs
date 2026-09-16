namespace VirtualMarina.Core.Api;

/// <summary>Why a slip id passed to <see cref="MarinaVisualizer.SetSelection(IEnumerable{string}, bool, VirtualMarina.Core.Camera.CameraAngle?)"/> was not selected.</summary>
public enum SlipSelectionRejection
{
    /// <summary>No slip has this id.</summary>
    NotFound,

    /// <summary>The slip is disabled (<c>IsDisabled</c>).</summary>
    Disabled,

    /// <summary>The slip is hidden (<c>IsVisible = false</c>).</summary>
    Hidden,

    /// <summary>The slip's status is excluded by the status filter.</summary>
    FilteredOut,
}

/// <summary>A slip id that <c>SetSelection</c> skipped.</summary>
/// <param name="SlipId">The id as passed in.</param>
/// <param name="Reason">Why it was skipped.</param>
public sealed record RejectedSlip(string SlipId, SlipSelectionRejection Reason);

/// <summary>Outcome of a selection change made through the API.</summary>
/// <param name="SelectedSlipIds">The selection after the call, in selection order (the last one is primary).</param>
/// <param name="Rejected">Ids that were skipped, with the reason.</param>
/// <param name="Changed">False when the selection was already exactly this.</param>
public sealed record SelectionResult(IReadOnlyList<string> SelectedSlipIds, IReadOnlyList<RejectedSlip> Rejected, bool Changed)
{
    /// <summary>Number of selected slips.</summary>
    public int Count => SelectedSlipIds.Count;

    /// <summary>True when nothing is selected.</summary>
    public bool IsEmpty => SelectedSlipIds.Count == 0;
}
